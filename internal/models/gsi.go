package models

// GsiSnapshotDTO GSI 战况快照统一外发载荷 DTO
type GsiSnapshotDTO struct {
	Timestamp int64  `json:"timestamp"`
	RawJson   string `json:"rawJson"`
}

// GsiStatusDTO GSI 接收状态 DTO
type GsiStatusDTO struct {
	Port          int    `json:"port"`
	IsListening   bool   `json:"isListening"`
	LastHeartbeat int64  `json:"lastHeartbeat"`
	LatestHero    string `json:"latestHero,omitempty"`
}
