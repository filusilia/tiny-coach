/**
 * 屏幕视觉与截屏 Store (src/stores/vision.ts)
 * 职责：集中管理截屏状态、最近图像缓存与 F10 全局热键联动
 */

import { defineStore } from 'pinia'
import { ref } from 'vue'
import { visionApi } from '../api/vision'
import type { CaptureResult } from '../../bindings/aegis-coach/internal/models/models'
import { detectBpHeroes } from '../services/vision/heroDetector'
import type { BpDetectionResult } from '../services/vision/types'

export const useVisionStore = defineStore('vision', () => {
  const lastCapture = ref<CaptureResult | null>(null)
  const isCapturing = ref(false)
  const isHotkeyActive = ref(false)
  const errorMessage = ref<string | null>(null)

  // 端侧视觉推理状态
  const lastDetection = ref<BpDetectionResult | null>(null)
  const isDetecting = ref(false)
  const detectionError = ref<string | null>(null)

  async function checkHotkeyStatus() {
    try {
      isHotkeyActive.value = await visionApi.isHotkeyActive()
    } catch {
      isHotkeyActive.value = false
    }
  }

  /**
   * 对截图数据执行端侧目标检测与槽位识别
   */
  async function runDetection(
    imageSource: string | HTMLImageElement,
    screenWidth: number,
    screenHeight: number,
    cropWidth?: number,
    cropHeight?: number,
  ): Promise<BpDetectionResult | null> {
    isDetecting.value = true
    detectionError.value = null
    try {
      const res = await detectBpHeroes(imageSource, screenWidth, screenHeight, cropWidth, cropHeight)
      lastDetection.value = res
      return res
    } catch (err: any) {
      console.error('[VisionStore] 端侧识别失败:', err)
      detectionError.value = err?.message || '端侧目标检测执行失败'
      return null
    } finally {
      isDetecting.value = false
    }
  }

  async function triggerCapture(): Promise<CaptureResult | null> {
    if (isCapturing.value) return null
    isCapturing.value = true
    errorMessage.value = null

    try {
      const res = await visionApi.captureTopScreen()
      if (res) {
        lastCapture.value = res
        if (!res.success) {
          errorMessage.value = res.errorMessage || '未能捕获游戏画面（请确认游戏窗口未最小化）'
        } else if (res.imageBase64) {
          // 自动触发端侧 ONNX 识别
          runDetection(
            res.imageBase64,
            res.screenWidth || 1920,
            res.screenHeight || 1080,
            res.cropWidth,
            res.cropHeight,
          )
        }
      }
      return res
    } catch (e: any) {
      errorMessage.value = e?.message || '截屏过程发生异常'
      return null
    } finally {
      isCapturing.value = false
    }
  }

  function handleCaptureEvent(payload: CaptureResult) {
    lastCapture.value = payload
    if (!payload.success) {
      errorMessage.value = payload.errorMessage || '未能捕获游戏画面（请确认游戏窗口未最小化）'
    } else {
      errorMessage.value = null
      if (payload.imageBase64) {
        // 自动触发端侧 ONNX 识别
        runDetection(
          payload.imageBase64,
          payload.screenWidth || 1920,
          payload.screenHeight || 1080,
          payload.cropWidth,
          payload.cropHeight,
        )
      }
    }
  }

  /**
   * 从用户本地文件上传并测试端侧识别
   */
  async function testWithLocalImageFile(file: File): Promise<BpDetectionResult | null> {
    return new Promise((resolve) => {
      const reader = new FileReader()
      reader.onload = async (e) => {
        const base64 = e.target?.result as string
        if (!base64) {
          resolve(null)
          return
        }

        const img = new Image()
        img.onload = async () => {
          // 若为全屏截图，设其为全屏尺寸；若为局部顶部截图，做合理推导
          const isTopCrop = img.naturalWidth / img.naturalHeight > 4
          const screenWidth = img.naturalWidth
          const screenHeight = isTopCrop ? Math.round(img.naturalWidth / (16 / 9)) : img.naturalHeight
          const cropWidth = img.naturalWidth
          const cropHeight = img.naturalHeight

          // 伪造或记录最近的截屏信息方便展示
          lastCapture.value = {
            success: true,
            screenWidth,
            screenHeight,
            cropX: 0,
            cropY: 0,
            cropWidth,
            cropHeight,
            imageBase64: base64,
            durationMs: 0,
            errorMessage: '',
          } as CaptureResult

          const result = await runDetection(img, screenWidth, screenHeight, cropWidth, cropHeight)
          resolve(result)
        }
        img.onerror = () => {
          detectionError.value = '无法加载该测试图片'
          resolve(null)
        }
        img.src = base64
      }
      reader.onerror = () => {
        detectionError.value = '读取本地图片失败'
        resolve(null)
      }
      reader.readAsDataURL(file)
    })
  }

  return {
    lastCapture,
    isCapturing,
    isHotkeyActive,
    errorMessage,
    lastDetection,
    isDetecting,
    detectionError,
    checkHotkeyStatus,
    triggerCapture,
    handleCaptureEvent,
    runDetection,
    testWithLocalImageFile,
  }
})
