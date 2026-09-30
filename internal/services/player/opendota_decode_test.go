package player

import (
	"encoding/json"
	"testing"

	"aegis-coach/internal/models"
)

// openDotaMatchesPayload 取自 OpenDota 真实响应字段形状：
// https://api.opendota.com/api/players/<account_id>/matches?limit=1
//
// 该契约使用 snake_case（match_id / player_slot / radiant_win / hero_id ...）。
// 本测试是防回归守卫：DTO 标签一旦被改回 camelCase，encoding/json 不会报错，
// 只会把字段静默解码为零值（大小写不敏感匹配但不会折叠下划线），
// 进而在无任何错误日志的情况下把玩家胜率算成 0%、英雄名算成「英雄#0」。
const openDotaMatchesPayload = `[{
	"match_id": 9022423471,
	"player_slot": 4,
	"radiant_win": true,
	"duration": 2044,
	"game_mode": 22,
	"lobby_type": 7,
	"hero_id": 53,
	"start_time": 1790728671,
	"version": 22,
	"kills": 20,
	"deaths": 3,
	"assists": 11,
	"average_rank": null,
	"leaver_status": 0,
	"party_size": 1,
	"hero_variant": 0
}]`

func TestOpenDotaMatchSummaryDecodesSnakeCase(t *testing.T) {
	var matches []models.OpenDotaMatchSummary
	if err := json.Unmarshal([]byte(openDotaMatchesPayload), &matches); err != nil {
		t.Fatalf("解码 OpenDota 战绩 JSON 失败: %v", err)
	}
	if len(matches) != 1 {
		t.Fatalf("期望解码出 1 场比赛，实际: %d", len(matches))
	}

	m := matches[0]

	// 关键字段：这些字段若标签风格回退为 camelCase，会全部静默变为零值。
	if m.MatchID != 9022423471 {
		t.Errorf("MatchID 解码错误：期望 9022423471，实际 %d", m.MatchID)
	}
	if m.PlayerSlot != 4 {
		t.Errorf("PlayerSlot 解码错误：期望 4，实际 %d（该字段为 0 会导致每场都被判定为天辉，胜率统计失真）", m.PlayerSlot)
	}
	if !m.RadiantWin {
		t.Errorf("RadiantWin 解码错误：期望 true，实际 %v", m.RadiantWin)
	}
	if m.HeroID != 53 {
		t.Errorf("HeroID 解码错误：期望 53，实际 %d（该字段为 0 会导致英雄名退化为「英雄#0」）", m.HeroID)
	}
	if m.StartTime != 1790728671 {
		t.Errorf("StartTime 解码错误：期望 1790728671，实际 %d", m.StartTime)
	}
	if m.PartySize != 1 {
		t.Errorf("PartySize 解码错误：期望 1，实际 %d", m.PartySize)
	}

	// 单词字段本就同名，用于证明解码器与 payload 本身都是健康的（区分「契约错」与「解析错」）。
	if m.Kills != 20 || m.Deaths != 3 || m.Assists != 11 {
		t.Errorf("KDA 解码错误：期望 20/3/11，实际 %d/%d/%d", m.Kills, m.Deaths, m.Assists)
	}
	if m.Duration != 2044 {
		t.Errorf("Duration 解码错误：期望 2044，实际 %d", m.Duration)
	}
}
