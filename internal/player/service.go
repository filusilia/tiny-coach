package player

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"time"

	"aegis-coach/internal/dota"
	"aegis-coach/internal/storage"
)

// CachedProfile 内存缓存包装实体
type CachedProfile struct {
	Timestamp time.Time
	Profile   *PlayerProfileAnalysis
}

// Service 玩家战绩网关与画像分析服务
type Service struct {
	httpClient     *http.Client
	storageManager *storage.Manager
	cacheMu        sync.RWMutex
	profileCache   map[string]CachedProfile
}

// NewService 构造玩家战绩服务实例
func NewService(sm *storage.Manager) *Service {
	return &Service{
		httpClient: &http.Client{
			Timeout: 12 * time.Second,
			Transport: &http.Transport{
				MaxIdleConns:        50,
				MaxIdleConnsPerHost: 10,
				IdleConnTimeout:     60 * time.Second,
			},
		},
		storageManager: sm,
		profileCache:   make(map[string]CachedProfile),
	}
}

// NormalizeAccountId 提取规范化 32 位账号 ID
func (s *Service) NormalizeAccountId(rawInput string) (string, error) {
	return NormalizeDotaAccountId(rawInput, s.httpClient)
}

// GetPlayerProfile 拉取并分析玩家近 20 场公开战绩 (带 10 分钟 TTL 内存缓存)
func (s *Service) GetPlayerProfile(accountIdInput string) (*PlayerProfileAnalysis, error) {
	normId, err := s.NormalizeAccountId(accountIdInput)
	if err != nil {
		normId = strings.TrimSpace(accountIdInput)
	}
	if normId == "" {
		return nil, fmt.Errorf("未提供有效的 Dota 2 玩家数字 ID")
	}

	// 1. 检查内存缓存 (10 分钟内直接复用)
	s.cacheMu.RLock()
	cached, found := s.profileCache[normId]
	s.cacheMu.RUnlock()
	if found && time.Since(cached.Timestamp) < 10*time.Minute {
		return cached.Profile, nil
	}

	// 2. 原生请求 OpenDota API
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
		return nil, fmt.Errorf("连接 OpenDota 战绩接口超时或失败: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return nil, fmt.Errorf("OpenDota 接口返回异常状态码 HTTP %d", resp.StatusCode)
	}

	var matches []OpenDotaMatchSummary
	if err := json.NewDecoder(resp.Body).Decode(&matches); err != nil {
		return nil, fmt.Errorf("解析 OpenDota 战绩 JSON 失败: %w", err)
	}

	if len(matches) == 0 {
		return nil, fmt.Errorf("未查询到该玩家的公开战绩数据 (请确认游戏内是否已开启“公开比赛数据”)")
	}

	// 3. 统计学数据抽取与英雄池画像计算
	analysis := s.analyzeMatches(normId, matches)

	// 4. 写入内存缓存
	s.cacheMu.Lock()
	s.profileCache[normId] = CachedProfile{
		Timestamp: time.Now(),
		Profile:   analysis,
	}
	s.cacheMu.Unlock()

	return analysis, nil
}

// analyzeMatches 提炼战绩数组，计算 KDA、胜率及 Top 5 英雄表现
func (s *Service) analyzeMatches(accountId string, matches []OpenDotaMatchSummary) *PlayerProfileAnalysis {
	totalWins := 0
	totalKills := 0
	totalDeaths := 0
	totalAssists := 0

	type heroAgg struct {
		matches int
		wins    int
		kills   int
		deaths  int
		assists int
	}
	heroMap := make(map[int]*heroAgg)

	for _, m := range matches {
		isRadiant := m.PlayerSlot < 128
		isWin := (isRadiant && m.RadiantWin) || (!isRadiant && !m.RadiantWin)

		if isWin {
			totalWins++
		}
		totalKills += m.Kills
		totalDeaths += m.Deaths
		totalAssists += m.Assists

		hid := m.HeroID
		agg, exists := heroMap[hid]
		if !exists {
			agg = &heroAgg{}
			heroMap[hid] = agg
		}
		agg.matches++
		if isWin {
			agg.wins++
		}
		agg.kills += m.Kills
		agg.deaths += m.Deaths
		agg.assists += m.Assists
	}

	n := len(matches)
	winRate := int(float64(totalWins) / float64(n) * 100.0)
	avgK := float64(int(float64(totalKills)/float64(n)*10+0.5)) / 10.0
	avgD := float64(int(float64(totalDeaths)/float64(n)*10+0.5)) / 10.0
	avgA := float64(int(float64(totalAssists)/float64(n)*10+0.5)) / 10.0

	var heroList []HeroPerformance
	for hid, val := range heroMap {
		heroName := dota.GetHeroChineseName(hid)
		hWinRate := int(float64(val.wins) / float64(val.matches) * 100.0)
		hAvgK := float64(int(float64(val.kills)/float64(val.matches)*10+0.5)) / 10.0
		hAvgD := float64(int(float64(val.deaths)/float64(val.matches)*10+0.5)) / 10.0
		hAvgA := float64(int(float64(val.assists)/float64(val.matches)*10+0.5)) / 10.0

		heroList = append(heroList, HeroPerformance{
			HeroID:     hid,
			HeroName:   heroName,
			Matches:    val.matches,
			Wins:       val.wins,
			WinRate:    hWinRate,
			AvgKills:   hAvgK,
			AvgDeaths:  hAvgD,
			AvgAssists: hAvgA,
		})
	}

	sort.Slice(heroList, func(i, j int) bool {
		if heroList[i].Matches != heroList[j].Matches {
			return heroList[i].Matches > heroList[j].Matches
		}
		return heroList[i].WinRate > heroList[j].WinRate
	})

	var topHeroes []HeroPerformance
	if len(heroList) > 5 {
		topHeroes = heroList[:5]
	} else {
		topHeroes = heroList
	}

	var topHeroDescs []string
	limit := 3
	if len(heroList) < 3 {
		limit = len(heroList)
	}
	for i := 0; i < limit; i++ {
		h := heroList[i]
		topHeroDescs = append(topHeroDescs, fmt.Sprintf("%s(%d场,胜率%d%%)", h.HeroName, h.Matches, h.WinRate))
	}
	heroStr := strings.Join(topHeroDescs, "、")
	if heroStr == "" {
		heroStr = "多面手"
	}

	tacticalSummary := fmt.Sprintf("近 %d 场排位总胜率 %d%% (场均 KDA: %.1f/%.1f/%.1f)，核心英雄偏好: %s。",
		n, winRate, avgK, avgD, avgA, heroStr)

	return &PlayerProfileAnalysis{
		AccountId:       accountId,
		TotalMatches:    n,
		Wins:            totalWins,
		Losses:          n - totalWins,
		WinRate:         winRate,
		AvgKills:        avgK,
		AvgDeaths:       avgD,
		AvgAssists:      avgA,
		TopHeroes:       topHeroes,
		RecentHeroes:    heroList,
		TacticalSummary: tacticalSummary,
		LastUpdated:     time.Now().Format("15:04:05"),
	}
}

// GetPlayerRecentMatches 原生获取玩家近期对局简报原始 JSON 字符串
func (s *Service) GetPlayerRecentMatches(accountId string) (string, error) {
	normId, err := s.NormalizeAccountId(accountId)
	if err != nil {
		normId = strings.TrimSpace(accountId)
	}
	if normId == "" {
		return "", fmt.Errorf("未提供有效的玩家数字 ID")
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
		return "", fmt.Errorf("拉取对局列表失败: %w", err)
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return "", fmt.Errorf("读取响应数据失败: %w", err)
	}

	return string(body), nil
}

// GetMatchDetails 获取单场历史比赛全量详情 (优先 0ms 本地磁盘不可变缓存)
func (s *Service) GetMatchDetails(matchId string) (string, error) {
	cleanMatchId := strings.TrimSpace(matchId)
	if cleanMatchId == "" {
		return "", fmt.Errorf("比赛 ID 不能为空")
	}

	// 1. 尝试从本地磁盘缓存读取
	cacheRoot := ""
	if s.storageManager != nil {
		cacheRoot, _ = s.storageManager.EnsureCacheDirectories("")
	}
	if cacheRoot == "" {
		cacheRoot = filepath.Join(storage.GetUserDocumentsDir(), "DotaAICoach")
	}
	matchDir := filepath.Join(cacheRoot, "matches")
	_ = os.MkdirAll(matchDir, 0755)

	cacheFilePath := filepath.Join(matchDir, fmt.Sprintf("match_%s.json", cleanMatchId))
	if data, err := os.ReadFile(cacheFilePath); err == nil && len(data) > 0 {
		return string(data), nil
	}

	// 2. 原生请求 OpenDota API
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	apiUrl := fmt.Sprintf("https://api.opendota.com/api/matches/%s", url.PathEscape(cleanMatchId))
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, apiUrl, nil)
	if err != nil {
		return "", err
	}
	req.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")

	resp, err := s.httpClient.Do(req)
	if err != nil {
		return "", fmt.Errorf("连接比赛详情接口超时或失败: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusOK {
		return "", fmt.Errorf("OpenDota 接口返回异常 HTTP %d", resp.StatusCode)
	}

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return "", fmt.Errorf("读取比赛详情失败: %w", err)
	}

	// 3. 异步落盘持久化
	go func(path string, content []byte) {
		_ = os.WriteFile(path, content, 0644)
	}(cacheFilePath, body)

	return string(body), nil
}
