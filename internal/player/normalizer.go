package player

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"net/url"
	"regexp"
	"strconv"
	"strings"
	"time"
	"unicode"
)

const ValveSteamID64Base uint64 = 76561197960265728

var (
	steam3Regex   = regexp.MustCompile(`(?i)U:1:(\d+)`)
	urlDotaRegex  = regexp.MustCompile(`(?i)(?:opendota\.com/players/|dotabuff\.com/players/)(\d+)`)
	steamUrlRegex = regexp.MustCompile(`(?i)steamcommunity\.com/profiles/(\d{17})`)
	steam64Regex  = regexp.MustCompile(`\b(7656119\d{10})\b`)
	idMatchRegex  = regexp.MustCompile(`\b(\d{6,10})\b`)
)

// ConvertSteamId64ToAccountId 64 位 Steam 社区 ID 转换为 32 位 Dota 2 账号数字 ID
func ConvertSteamId64ToAccountId(steamId64Str string) string {
	raw := strings.TrimSpace(steamId64Str)
	if raw == "" {
		return ""
	}
	if len(raw) < 15 {
		return raw
	}
	val, err := strconv.ParseUint(raw, 10, 64)
	if err != nil {
		return raw
	}
	if val > ValveSteamID64Base {
		return strconv.FormatUint(val-ValveSteamID64Base, 10)
	}
	return raw
}

// CleanUnicodeAndEmoji 彻底清洗零宽不可见字符、Emoji、控制符，并将全角数字转换为半角数字
func CleanUnicodeAndEmoji(input string) string {
	var sb strings.Builder
	for _, r := range input {
		// 1. 全角数字 (U+FF10 ~ U+FF19) 转为半角 '0' ~ '9'
		if r >= 0xFF10 && r <= 0xFF19 {
			sb.WriteRune('0' + (r - 0xFF10))
			continue
		}
		// 2. 忽略零宽字符与控制字符
		if r == 0x200B || r == 0x200C || r == 0x200D || r == 0xFEFF || (r >= 0x0000 && r <= 0x001F) {
			continue
		}
		// 3. 忽略 Emoji 与杂项符号区
		if (r >= 0x1F300 && r <= 0x1F9FF) || (r >= 0x2600 && r <= 0x26FF) || (r >= 0x2700 && r <= 0x27BF) || (r >= 0x1F1E6 && r <= 0x1F1FF) {
			sb.WriteRune(' ')
			continue
		}
		sb.WriteRune(r)
	}
	return strings.TrimSpace(sb.String())
}

// NormalizeDotaAccountId 高鲁棒性 Dota 2 玩家数字 ID 提取器
func NormalizeDotaAccountId(rawInput string, client *http.Client) (string, error) {
	if strings.TrimSpace(rawInput) == "" {
		return "", fmt.Errorf("输入的玩家账号或昵称为空")
	}

	cleaned := CleanUnicodeAndEmoji(rawInput)
	if cleaned == "" {
		return "", fmt.Errorf("输入内容清洗后无有效字符")
	}

	// 1. 匹配 Steam3 格式：如 [U:1:12345678] 或 U:1:12345678
	if m := steam3Regex.FindStringSubmatch(cleaned); len(m) > 1 {
		return m[1], nil
	}

	// 2. 匹配主流战绩链接 (OpenDota / Dotabuff)
	if m := urlDotaRegex.FindStringSubmatch(cleaned); len(m) > 1 {
		return m[1], nil
	}

	// 3. 匹配 Steam 社区个人主页 URL
	if m := steamUrlRegex.FindStringSubmatch(cleaned); len(m) > 1 {
		return ConvertSteamId64ToAccountId(m[1]), nil
	}

	// 4. 匹配 17 位 SteamID64
	if m := steam64Regex.FindStringSubmatch(cleaned); len(m) > 1 {
		return ConvertSteamId64ToAccountId(m[1]), nil
	}

	// 5. 提取 6~10 位数字串 (32 位 Dota 2 好友 ID)
	if m := idMatchRegex.FindStringSubmatch(cleaned); len(m) > 1 {
		return m[1], nil
	}

	// 6. 提取纯数字 (4~10 位)
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

	// 7. 降级：若用户输入的是纯玩家昵称（长度 >= 2），尝试调用 OpenDota Search API
	searchName := strings.TrimSpace(cleaned)
	if len([]rune(searchName)) >= 2 && client != nil {
		ctx, cancel := context.WithTimeout(context.Background(), 4*time.Second)
		defer cancel()

		searchUrl := fmt.Sprintf("https://api.opendota.com/api/search?q=%s", url.QueryEscape(searchName))
		req, err := http.NewRequestWithContext(ctx, http.MethodGet, searchUrl, nil)
		if err == nil {
			req.Header.Set("User-Agent", "AegisCoach/2.0 (Dota2-Tactical-Assistant; Native-Go)")
			resp, err := client.Do(req)
			if err == nil {
				defer resp.Body.Close()
				if resp.StatusCode == http.StatusOK {
					var results []struct {
						AccountId int64 `json:"account_id"`
					}
					if json.NewDecoder(resp.Body).Decode(&results) == nil && len(results) > 0 && results[0].AccountId > 0 {
						return strconv.FormatInt(results[0].AccountId, 10), nil
					}
				}
			}
		}
	}

	return "", fmt.Errorf("未能从输入中识别出有效的 Dota 2 玩家数字 ID")
}
