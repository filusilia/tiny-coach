//go:build windows

package windows

import (
	"log"
	"runtime"
	"sync"
	"syscall"
	"time"
	"unsafe"
)

var (
	modkernel32 = syscall.NewLazyDLL("kernel32.dll")

	procGetCurrentThreadId = modkernel32.NewProc("GetCurrentThreadId")
	procRegisterHotKey     = moduser32.NewProc("RegisterHotKey")
	procUnregisterHotKey   = moduser32.NewProc("UnregisterHotKey")
	procGetMessageW        = moduser32.NewProc("GetMessageW")
	procPostThreadMessageW = moduser32.NewProc("PostThreadMessageW")
)

const (
	WM_HOTKEY    = 0x0312
	WM_QUIT      = 0x0012
	VK_F10       = 0x79
	MOD_NOREPEAT = 0x4000
)

type POINT struct {
	X int32
	Y int32
}

type MSG struct {
	Hwnd    uintptr
	Message uint32
	WParam  uintptr
	LParam  uintptr
	Time    uint32
	Pt      POINT
}

// HotkeyManager Windows 全局热键管理器
type HotkeyManager struct {
	mu              sync.Mutex
	threadID        uint32
	running         bool
	onTrigger       func()
	lastTriggerTime int64
}

var (
	hotkeyManagerInstance *HotkeyManager
	hotkeyManagerOnce     sync.Once
)

func GetHotkeyManager() *HotkeyManager {
	hotkeyManagerOnce.Do(func() {
		hotkeyManagerInstance = &HotkeyManager{}
	})
	return hotkeyManagerInstance
}

// StartF10Listener 启动全局 F10 热键监听器
// 铁律：必须在专属 Goroutine 中调用 runtime.LockOSThread() 绑定 OS 线程，驱动 GetMessage 消息泵
func (m *HotkeyManager) StartF10Listener(onTrigger func()) bool {
	m.mu.Lock()
	if m.running {
		m.onTrigger = onTrigger
		m.mu.Unlock()
		return true
	}
	m.running = true
	m.onTrigger = onTrigger
	m.mu.Unlock()

	readyChan := make(chan bool, 1)

	go func() {
		// ★ 严格落实要求：必须锁定 OS 线程，防止协程调度导致丢失 Windows 线程消息
		runtime.LockOSThread()
		defer runtime.UnlockOSThread()

		tid, _, _ := procGetCurrentThreadId.Call()
		m.mu.Lock()
		m.threadID = uint32(tid)
		m.mu.Unlock()

		const hotkeyID = 0xACE1 // Aegis Coach Event 1

		// 注册全局 F10 热键
		ret, _, err := procRegisterHotKey.Call(0, uintptr(hotkeyID), uintptr(MOD_NOREPEAT), uintptr(VK_F10))
		if ret == 0 {
			// 若带 MOD_NOREPEAT 失败则回退无修饰符注册
			ret, _, err = procRegisterHotKey.Call(0, uintptr(hotkeyID), 0, uintptr(VK_F10))
		}

		if ret == 0 {
			log.Printf("[HotkeyManager] 注册全局 F10 热键失败: %v", err)
			readyChan <- false
			m.mu.Lock()
			m.running = false
			m.mu.Unlock()
			return
		}
		defer procUnregisterHotKey.Call(0, uintptr(hotkeyID))

		log.Printf("[HotkeyManager] ✅ 全局 F10 热键已成功锁定至 OS 线程 (TID: %d)", tid)
		readyChan <- true

		var msg MSG
		for {
			// 阻塞等待线程消息队列
			r, _, _ := procGetMessageW.Call(uintptr(unsafe.Pointer(&msg)), 0, 0, 0)
			if int32(r) <= 0 || msg.Message == WM_QUIT {
				break
			}

			if msg.Message == WM_HOTKEY && msg.WParam == uintptr(hotkeyID) {
				now := time.Now().UnixMilli()
				m.mu.Lock()
				if now-m.lastTriggerTime < 600 {
					m.mu.Unlock()
					continue // 严格落实要求 5：600ms 防抖丢弃
				}
				m.lastTriggerTime = now
				handler := m.onTrigger
				m.mu.Unlock()

				if handler != nil {
					// 异步执行回调，坚决不阻塞消息泵自身
					go handler()
				}
			}
		}

		log.Printf("[HotkeyManager] 全局 F10 热键消息循环已退出")
		m.mu.Lock()
		m.running = false
		m.threadID = 0
		m.mu.Unlock()
	}()

	return <-readyChan
}

// Stop 停止全局热键监听
func (m *HotkeyManager) Stop() {
	m.mu.Lock()
	defer m.mu.Unlock()

	if !m.running || m.threadID == 0 {
		return
	}

	// 投递 WM_QUIT 给热键线程以退出 GetMessage 循环
	procPostThreadMessageW.Call(uintptr(m.threadID), uintptr(WM_QUIT), 0, 0)
}
