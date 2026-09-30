package player

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
	steam3Regex    = regexp.MustCompile(`(?i)U:1:(\d+)`)
	urlDotaRegex   = regexp.MustCompile(`(?i)(?:opendota\.com/players/|dotabuff\.com/players/)(\d+)`)
	steamUrlRegex  = regexp.MustCompile(`(?i)steamcommunity\.com/profiles/(\d{17})`)
	steam64Regex   = regexp.MustCompile(`\b(7656119\d{10})\b`)
	idMatchRegex   = regexp.MustCompile(`\b(\d{6,10})\b`)
	numericIdRegex = regexp.MustCompile(`^[0-9]{1,20}$`)
)

func isValidNumericId(id string) bool {
	return numericIdRegex.MatchString(strings.TrimSpace(id))
}

// cachedProfileWrapper 内存缓存包装实体
type cachedProfileWrapper struct {
	Timestamp time.Time
	Profile   *models.PlayerProfileAnalysis
}

// singleFlightCall 代表一个正在进行的调用
type singleFlightCall struct {
	wg  sync.WaitGroup
	val any
	err error
}

// singleFlightGroup 实现轻量级无外部第三方依赖的并发任务合并（遵循铁律 10）
type singleFlightGroup struct {
	mu sync.Mutex
	m  map[string]*singleFlightCall
}

func (g *singleFlightGroup) Do(key string, fn func() (any, error)) (any, error) {
	g.mu.Lock()
	if g.m == nil {
		g.m = make(map[string]*singleFlightCall)
	}
	if c, ok := g.m[key]; ok {
		g.mu.Unlock()
		c.wg.Wait()
		return c.val, c.err
	}
	c := new(singleFlightCall)
	c.wg.Add(1)
	g.m[key] = c
	g.mu.Unlock()

	c.val, c.err = fn()
	c.wg.Done()

	g.mu.Lock()
	delete(g.m, key)
	g.mu.Unlock()

	return c.val, c.err
}

// PlayerService 玩家战绩网关与技战术画像分析服务 (唯一业务与缓存权威)
type PlayerService struct {
	httpClient     *http.Client
	storageManager *storage.Manager
	cacheMu        sync.RWMutex
	profileCache   map[string]cachedProfileWrapper
	sfGroup        singleFlightGroup
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
			storageManager: storage.GetManager(), // ★ 使用全局单例，与 SystemService 共享
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
		return nil, fmt.Errorf("解析玩家账号 ID 失败: %w", err)
	}
	if !isValidNumericId(normId) {
		return nil, fmt.Errorf("未能识别出有效的 Dota 2 数字玩家 ID: %s", accountIdInput)
	}

	// 1. 内存缓存检查 (10 分钟 TTL)
	s.cacheMu.RLock()
	cached, found := s.profileCache[normId]
	s.cacheMu.RUnlock()
	if found && time.Since(cached.Timestamp) < 10*time.Minute {
		return cached.Profile, nil
	}

	// 2. 并发合并 (Single-Flight): 防止多组件瞬间并发触发重复 OpenDota API 请求 (铁律 10)
	res, err := s.sfGroup.Do("profile:"+normId, func() (any, error) {
		// 二次检查内存缓存
		s.cacheMu.RLock()
		cached, found := s.profileCache[normId]
		s.cacheMu.RUnlock()
		if found && time.Since(cached.Timestamp) < 10*time.Minute {
			return cached.Profile, nil
		}

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
	})

	if err != nil {
		return nil, err
	}
	return res.(*models.PlayerProfileAnalysis), nil
}

// GetMatchDetails 查询单场公开比赛详情原始 JSON (优先本地磁盘不可变缓存)
func (s *PlayerService) GetMatchDetails(matchId string) (string, error) {
	cleanId := strings.TrimSpace(matchId)
	if !isValidNumericId(cleanId) {
		return "", fmt.Errorf("无效的比赛 ID，必须为 1~20 位纯数字: %s", matchId)
	}

	// ★ 1. 优先检查本地磁盘不可变缓存（采用 filepath.Base 严密防御路径遍历）
	cacheDir := filepath.Join(s.storageManager.GetCacheDirectory(), "matches")
	_ = os.MkdirAll(cacheDir, 0755)
	cacheFile := filepath.Join(cacheDir, fmt.Sprintf("match_%s.json", filepath.Base(cleanId)))

	if data, err := os.ReadFile(cacheFile); err == nil && len(data) > 0 {
		log.Printf("[PlayerService] 命中本地比赛缓存: %s", cacheFile)
		return string(data), nil
	}

	// ★ 2. 并发合并 (Single-Flight): 无论前端有几个组件同时加载此比赛，只发一次网络请求并安全落盘 (铁律 10)
	res, err := s.sfGroup.Do("match:"+cleanId, func() (any, error) {
		// 二次检查本地磁盘缓存
		if data, err := os.ReadFile(cacheFile); err == nil && len(data) > 0 {
			return string(data), nil
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

		// ★ 3. 落盘写入本地不可变缓存（在 singleFlight 独占块内写入，杜绝 Windows 多并发写同一文件的句柄竞争）
		if writeErr := os.WriteFile(cacheFile, body, 0644); writeErr != nil {
			log.Printf("[PlayerService] 写入比赛缓存失败 %s: %v", cacheFile, writeErr)
		}

		return string(body), nil
	})

	if err != nil {
		return "", err
	}
	return res.(string), nil
}

// GetPlayerRecentMatches 查询玩家近 20 场比赛原始列表 JSON
func (s *PlayerService) GetPlayerRecentMatches(accountId string) (string, error) {
	normId, err := s.NormalizeAccountId(accountId)
	if err != nil {
		return "", fmt.Errorf("解析玩家账号 ID 失败: %w", err)
	}
	if !isValidNumericId(normId) {
		return "", fmt.Errorf("未能识别出有效的 Dota 2 数字玩家 ID: %s", accountId)
	}

	res, err := s.sfGroup.Do("recent:"+normId, func() (any, error) {
		ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
		defer cancel()

		apiUrl := fmt.Sprintf("https://api.opendota.com/api/players/%s/matches?limit=100&offset=0", url.PathEscape(normId))
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
	})

	if err != nil {
		return "", err
	}
	return res.(string), nil
}

// GetPlayerCard 查询玩家公开个人主页原始 JSON (昵称、头像、天梯段位等)
//
// 统一出口约束（铁律 6 / 10）：前端严禁直连 OpenDota，个人主页信息一律经本方法获取。
func (s *PlayerService) GetPlayerCard(accountId string) (string, error) {
	normId, err := s.NormalizeAccountId(accountId)
	if err != nil {
		return "", fmt.Errorf("解析玩家账号 ID 失败: %w", err)
	}
	if !isValidNumericId(normId) {
		return "", fmt.Errorf("未能识别出有效的 Dota 2 数字玩家 ID: %s", accountId)
	}

	res, err := s.sfGroup.Do("card:"+normId, func() (any, error) {
		ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
		defer cancel()

		apiUrl := fmt.Sprintf("https://api.opendota.com/api/players/%s", url.PathEscape(normId))
		req, err := http.NewRequestWithContext(ctx, http.MethodGet, apiUrl, nil)
		if err != nil {
			return "", err
		}
		req.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")

		resp, err := s.httpClient.Do(req)
		if err != nil {
			return "", fmt.Errorf("拉取玩家个人主页失败: %w", err)
		}
		defer func() { _ = resp.Body.Close() }()

		if resp.StatusCode != http.StatusOK {
			return "", fmt.Errorf("玩家个人主页接口返回异常 HTTP %d", resp.StatusCode)
		}

		limited := io.LimitReader(resp.Body, maxPlayerApiBytes)
		body, err := io.ReadAll(limited)
		if err != nil {
			return "", err
		}
		return string(body), nil
	})

	if err != nil {
		return "", err
	}
	return res.(string), nil
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

	// 按时间倒序提取最近使用过的英雄（最近出场优先，去重保留最多 5 个）
	var recentHeroes []models.HeroPerformance
	seenRecentHero := make(map[int]bool)
	for _, m := range matches {
		if m.HeroID <= 0 || seenRecentHero[m.HeroID] {
			continue
		}
		seenRecentHero[m.HeroID] = true
		if acc, ok := heroMap[m.HeroID]; ok {
			winRate := 0
			if acc.matches > 0 {
				winRate = int(float64(acc.wins) / float64(acc.matches) * 100.0)
			}
			hName := dota.GetHeroChineseName(m.HeroID)
			recentHeroes = append(recentHeroes, models.HeroPerformance{
				HeroID:     m.HeroID,
				HeroName:   hName,
				Matches:    acc.matches,
				Wins:       acc.wins,
				WinRate:    winRate,
				AvgKills:   roundFloat(float64(acc.kills)/float64(acc.matches), 1),
				AvgDeaths:  roundFloat(float64(acc.deaths)/float64(acc.matches), 1),
				AvgAssists: roundFloat(float64(acc.assists)/float64(acc.matches), 1),
			})
			if len(recentHeroes) >= 5 {
				break
			}
		}
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
		RecentHeroes:    recentHeroes,
		TacticalSummary: summarySb.String(),
		LastUpdated:     time.Now().Format("2006-01-02 15:04:05"),
	}
}

func (s *PlayerService) persistProfileAsync(accountId string, profile *models.PlayerProfileAnalysis) {
	cleanId := strings.TrimSpace(accountId)
	if !isValidNumericId(cleanId) {
		log.Printf("[PlayerService] 跳过非法账号 ID 画像持久化: %s", accountId)
		return
	}
	baseDir := s.storageManager.GetCacheDirectory()
	matchesDir := filepath.Join(baseDir, "matches")
	if err := os.MkdirAll(matchesDir, 0755); err != nil {
		log.Printf("[PlayerService] 创建缓存目录失败: %v", err)
		return
	}

	path := filepath.Join(matchesDir, fmt.Sprintf("profile_%s.json", filepath.Base(cleanId)))
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
