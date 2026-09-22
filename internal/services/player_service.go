package services

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"
	"unicode"

	"aegis-coach/internal/dota"
	"aegis-coach/internal/models"
	"aegis-coach/internal/storage"
)

const (
	valveSteamID64Base uint64 = 76561197960265728
	maxPlayerApiBytes  int64  = 4 * 1024 * 1024 // 4 MiB 响应体上限
)

var (
	steam3Regex   = regexp.MustCompile(`(?i)U:1:(\d+)`)
	urlDotaRegex  = regexp.MustCompile(`(?i)(?:opendota\.com/players/|dotabuff\.com/players/)(\d+)`)
	steamUrlRegex = regexp.MustCompile(`(?i)steamcommunity\.com/profiles/(\d{17})`)
	steam64Regex  = regexp.MustCompile(`\b(7656119\d{10})\b`)
	idMatchRegex  = regexp.MustCompile(`\b(\d{6,10})\b`)
)

// cachedProfileWrapper 内存缓存包装实体
type cachedProfileWrapper struct {
	Timestamp time.Time
	Profile   *models.PlayerProfileAnalysis
}

// PlayerService 玩家战绩网关与技战术画像分析服务 (唯一业务与缓存权威)
type PlayerService struct {
	httpClient     *http.Client
	storageManager *storage.Manager
	cacheMu        sync.RWMutex
	profileCache   map[string]cachedProfileWrapper
}

var (
	playerServiceInstance *PlayerService
	playerServiceOnce     sync.Once
)

// GetPlayerService 获取 PlayerService 单例
func GetPlayerService() *PlayerService {
	playerServiceOnce.Do(func() {
		playerServiceInstance = &PlayerService{
			httpClient: &http.Client{
				Timeout: 12 * time.Second,
				Transport: &http.Transport{
					MaxIdleConns:        50,
					MaxIdleConnsPerHost: 10,
					IdleConnTimeout:     60 * time.Second,
				},
			},
			storageManager: storage.NewManager(),
			profileCache:   make(map[string]cachedProfileWrapper),
		}
	})
	return playerServiceInstance
}

// NormalizeAccountId 提取规范化 32 位数字账号 ID
func (s *PlayerService) NormalizeAccountId(rawInput string) (string, error) {
	if strings.TrimSpace(rawInput) == "" {
		return "", fmt.Errorf("输入的玩家账号或昵称为空")
	}

	cleaned := cleanUnicodeAndEmoji(rawInput)
	if cleaned == "" {
		return "", fmt.Errorf("输入内容清洗后无有效字符")
	}

	if m := steam3Regex.FindStringSubmatch(cleaned); len(m) > 1 {
		return m[1], nil
	}
	if m := urlDotaRegex.FindStringSubmatch(cleaned); len(m) > 1 {
		return m[1], nil
	}
	if m := steamUrlRegex.FindStringSubmatch(cleaned); len(m) > 1 {
		return convertSteamId64ToAccountId(m[1]), nil
	}
	if m := steam64Regex.FindStringSubmatch(cleaned); len(m) > 1 {
		return convertSteamId64ToAccountId(m[1]), nil
	}
	if m := idMatchRegex.FindStringSubmatch(cleaned); len(m) > 1 {
		return m[1], nil
	}

	var digitsOnly strings.Builder
	for _, r := range cleaned {
		if unicode.IsDigit(r) {
			digitsOnly.WriteRune(r)
		}
	}
	digitStr := digitsOnly.String()
	if len(digitStr) >= 4 && len(digitStr) <= 10 {
		return digitStr, nil
	}

	// 降级查询昵称
	searchName := strings.TrimSpace(cleaned)
	if len([]rune(searchName)) >= 2 {
		ctx, cancel := context.WithTimeout(context.Background(), 4*time.Second)
		defer cancel()

		searchUrl := fmt.Sprintf("https://api.opendota.com/api/search?q=%s", url.QueryEscape(searchName))
		req, err := http.NewRequestWithContext(ctx, http.MethodGet, searchUrl, nil)
		if err == nil {
			req.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")
			resp, err := s.httpClient.Do(req)
			if err == nil {
				defer func() { _ = resp.Body.Close() }()
				if resp.StatusCode == http.StatusOK {
					limited := io.LimitReader(resp.Body, maxPlayerApiBytes)
					var results []struct {
						AccountId int64 `json:"account_id"`
					}
					if json.NewDecoder(limited).Decode(&results) == nil && len(results) > 0 && results[0].AccountId > 0 {
						return strconv.FormatInt(results[0].AccountId, 10), nil
					}
				}
			}
		}
	}

	return "", fmt.Errorf("未能从输入中识别出有效的 Dota 2 玩家数字 ID")
}

// GetPlayerProfile 拉取并分析玩家近 20 场公开战绩 (Go 后端单例持有 10 分钟 TTL 与本地落盘权威)
func (s *PlayerService) GetPlayerProfile(accountIdInput string) (*models.PlayerProfileAnalysis, error) {
	normId, err := s.NormalizeAccountId(accountIdInput)
	if err != nil {
		normId = strings.TrimSpace(accountIdInput)
	}
	if normId == "" {
		return nil, fmt.Errorf("未提供有效的 Dota 2 玩家数字 ID")
	}

	// 1. 内存缓存检查 (10 分钟 TTL)
	s.cacheMu.RLock()
	cached, found := s.profileCache[normId]
	s.cacheMu.RUnlock()
	if found && time.Since(cached.Timestamp) < 10*time.Minute {
		return cached.Profile, nil
	}

	// 2. OpenDota API 请求 (带 MaxBytesReader 限流)
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()

	apiUrl := fmt.Sprintf("https://api.opendota.com/api/players/%s/matches?limit=20&offset=0", url.PathEscape(normId))
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, apiUrl, nil)
	if err != nil {
		return nil, fmt.Errorf("构造战绩请求失败: %w", err)
	}
	req.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")

	resp, err := s.httpClient.Do(req)
	if err != nil {
		return nil, fmt.Errorf("连接 OpenDota 战绩接口失败: %w", err)
	}
	defer func() { _ = resp.Body.Close() }()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("OpenDota 接口返回异常状态码 HTTP %d", resp.StatusCode)
	}

	limited := io.LimitReader(resp.Body, maxPlayerApiBytes)
	var matches []models.OpenDotaMatchSummary
	if err := json.NewDecoder(limited).Decode(&matches); err != nil {
		return nil, fmt.Errorf("解析 OpenDota 战绩 JSON 失败: %w", err)
	}

	if len(matches) == 0 {
		return nil, fmt.Errorf("该玩家近 20 场战绩为空或个人资料未公开")
	}

	// 3. 统计计算 (所有战术计算在 Go 侧完成)
	analysis := s.calculateProfile(normId, matches)

	// 4. 写入内存缓存
	s.cacheMu.Lock()
	s.profileCache[normId] = cachedProfileWrapper{
		Timestamp: time.Now(),
		Profile:   analysis,
	}
	s.cacheMu.Unlock()

	// 5. 异步落盘持久化 (带错误日志)
	go s.persistProfileAsync(normId, analysis)

	return analysis, nil
}

// GetMatchDetails 查询单场公开比赛详情原始 JSON
func (s *PlayerService) GetMatchDetails(matchId string) (string, error) {
	cleanId := strings.TrimSpace(matchId)
	if cleanId == "" {
		return "", fmt.Errorf("比赛 ID 不能为空")
	}

	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()

	apiUrl := fmt.Sprintf("https://api.opendota.com/api/matches/%s", url.PathEscape(cleanId))
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, apiUrl, nil)
	if err != nil {
		return "", err
	}
	req.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")

	resp, err := s.httpClient.Do(req)
	if err != nil {
		return "", err
	}
	defer func() { _ = resp.Body.Close() }()

	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("查询单场比赛失败 HTTP %d", resp.StatusCode)
	}

	limited := io.LimitReader(resp.Body, maxPlayerApiBytes)
	body, err := io.ReadAll(limited)
	if err != nil {
		return "", err
	}
	return string(body), nil
}

// GetPlayerRecentMatches 查询玩家近 20 场比赛原始列表 JSON
func (s *PlayerService) GetPlayerRecentMatches(accountId string) (string, error) {
	normId, err := s.NormalizeAccountId(accountId)
	if err != nil {
		normId = strings.TrimSpace(accountId)
	}
	if normId == "" {
		return "", fmt.Errorf("玩家数字 ID 不能为空")
	}

	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()

	apiUrl := fmt.Sprintf("https://api.opendota.com/api/players/%s/matches?limit=20&offset=0", url.PathEscape(normId))
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, apiUrl, nil)
	if err != nil {
		return "", err
	}
	req.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")

	resp, err := s.httpClient.Do(req)
	if err != nil {
		return "", err
	}
	defer func() { _ = resp.Body.Close() }()

	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("拉取近期比赛列表失败 HTTP %d", resp.StatusCode)
	}

	limited := io.LimitReader(resp.Body, maxPlayerApiBytes)
	body, err := io.ReadAll(limited)
	if err != nil {
		return "", err
	}
	return string(body), nil
}

func (s *PlayerService) calculateProfile(accountId string, matches []models.OpenDotaMatchSummary) *models.PlayerProfileAnalysis {
	total := len(matches)
	wins := 0
	totKills := 0
	totDeaths := 0
	totAssists := 0

	type heroAcc struct {
		matches int
		wins    int
		kills   int
		deaths  int
		assists int
	}
	heroMap := make(map[int]*heroAcc)

	for _, m := range matches {
		isRadiant := m.PlayerSlot < 128
		won := (isRadiant && m.RadiantWin) || (!isRadiant && !m.RadiantWin)
		if won {
			wins++
		}
		totKills += m.Kills
		totDeaths += m.Deaths
		totAssists += m.Assists

		acc, exists := heroMap[m.HeroID]
		if !exists {
			acc = &heroAcc{}
			heroMap[m.HeroID] = acc
		}
		acc.matches++
		if won {
			acc.wins++
		}
		acc.kills += m.Kills
		acc.deaths += m.Deaths
		acc.assists += m.Assists
	}

	var heroList []models.HeroPerformance
	for hid, acc := range heroMap {
		winRate := 0
		if acc.matches > 0 {
			winRate = int(float64(acc.wins) / float64(acc.matches) * 100.0)
		}
		hName := dota.GetHeroChineseName(hid)
		heroList = append(heroList, models.HeroPerformance{
			HeroID:     hid,
			HeroName:   hName,
			Matches:    acc.matches,
			Wins:       acc.wins,
			WinRate:    winRate,
			AvgKills:   roundFloat(float64(acc.kills)/float64(acc.matches), 1),
			AvgDeaths:  roundFloat(float64(acc.deaths)/float64(acc.matches), 1),
			AvgAssists: roundFloat(float64(acc.assists)/float64(acc.matches), 1),
		})
	}

	sort.Slice(heroList, func(i, j int) bool {
		if heroList[i].Matches != heroList[j].Matches {
			return heroList[i].Matches > heroList[j].Matches
		}
		return heroList[i].WinRate > heroList[j].WinRate
	})

	topHeroes := heroList
	if len(topHeroes) > 5 {
		topHeroes = topHeroes[:5]
	}

	overallWinRate := 0
	if total > 0 {
		overallWinRate = int(float64(wins) / float64(total) * 100.0)
	}

	var summarySb strings.Builder
	summarySb.WriteString(fmt.Sprintf("近 %d 场公开战绩胜率 %d%% (胜 %d / 负 %d)。场均 KDA: %.1f / %.1f / %.1f。",
		total, overallWinRate, wins, total-wins,
		float64(totKills)/float64(total), float64(totDeaths)/float64(total), float64(totAssists)/float64(total)))
	if len(topHeroes) > 0 {
		summarySb.WriteString(" 最常使用英雄: ")
		var heroSnippets []string
		for _, h := range topHeroes {
			heroSnippets = append(heroSnippets, fmt.Sprintf("%s (%d场 %d%%胜率)", h.HeroName, h.Matches, h.WinRate))
		}
		summarySb.WriteString(strings.Join(heroSnippets, ", "))
	}

	return &models.PlayerProfileAnalysis{
		AccountId:       accountId,
		TotalMatches:    total,
		Wins:            wins,
		Losses:          total - wins,
		WinRate:         overallWinRate,
		AvgKills:        roundFloat(float64(totKills)/float64(total), 1),
		AvgDeaths:       roundFloat(float64(totDeaths)/float64(total), 1),
		AvgAssists:      roundFloat(float64(totAssists)/float64(total), 1),
		TopHeroes:       topHeroes,
		RecentHeroes:    topHeroes,
		TacticalSummary: summarySb.String(),
		LastUpdated:     time.Now().Format("2006-01-02 15:04:05"),
	}
}

func (s *PlayerService) persistProfileAsync(accountId string, profile *models.PlayerProfileAnalysis) {
	baseDir := s.storageManager.GetCacheDirectory()
	matchesDir := filepath.Join(baseDir, "matches")
	if err := os.MkdirAll(matchesDir, 0755); err != nil {
		log.Printf("[PlayerService] 创建缓存目录失败: %v", err)
		return
	}

	path := filepath.Join(matchesDir, fmt.Sprintf("profile_%s.json", accountId))
	data, err := json.MarshalIndent(profile, "", "  ")
	if err != nil {
		log.Printf("[PlayerService] 序列化战绩画像失败: %v", err)
		return
	}

	if err := os.WriteFile(path, data, 0644); err != nil {
		log.Printf("[PlayerService] 异步落盘比赛画像失败 %s: %v", path, err)
	}
}

func convertSteamId64ToAccountId(steamId64Str string) string {
	raw := strings.TrimSpace(steamId64Str)
	if raw == "" || len(raw) < 15 {
		return raw
	}
	val, err := strconv.ParseUint(raw, 10, 64)
	if err != nil || val <= valveSteamID64Base {
		return raw
	}
	return strconv.FormatUint(val-valveSteamID64Base, 10)
}

func cleanUnicodeAndEmoji(input string) string {
	var sb strings.Builder
	for _, r := range input {
		if r >= 0xFF10 && r <= 0xFF19 {
			sb.WriteRune('0' + (r - 0xFF10))
			continue
		}
		if r == 0x200B || r == 0x200C || r == 0x200D || r == 0xFEFF || (r >= 0x0000 && r <= 0x001F) {
			continue
		}
		if (r >= 0x1F300 && r <= 0x1F9FF) || (r >= 0x2600 && r <= 0x26FF) || (r >= 0x2700 && r <= 0x27BF) || (r >= 0x1F1E6 && r <= 0x1F1FF) {
			sb.WriteRune(' ')
			continue
		}
		sb.WriteRune(r)
	}
	return strings.TrimSpace(sb.String())
}

func roundFloat(val float64, precision int) float64 {
	format := fmt.Sprintf("%%.%df", precision)
	s := fmt.Sprintf(format, val)
	res, _ := strconv.ParseFloat(s, 64)
	return res
}
