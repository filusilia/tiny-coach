/**
 * 端侧 ONNX Runtime Web 英雄目标检测引擎 (src/services/vision/heroDetector.ts)
 * 职责：
 * 1. 严格对齐 dota2-mentor-pack 的输入归一化、Tensor 维度解析与 NMS (IOU 0.45 / Conf 0.06)
 * 2. 显式设置 ort.env.wasm.wasmPaths = '/ort/' 与 numThreads = 1，规避 Wasm 线程与魔数错误
 * 3. 采用“虚拟全屏 Letterbox”几何对齐算法，将顶部 18% ROI 映射至 640x640 规范输入并 1:1 反算物理坐标
 * 4. 匹配槽位，输出天辉 5 位与夜魇 5 位英雄结构
 */

import * as ort from 'onnxruntime-web'
import { YOLO_HERO_CLASSES } from './heroClasses'
import { matchDetectionsToSlots } from './slotOverrides'
import type { BpDetectionResult, RawDetectionBox } from './types'

const MODEL_PATH = '/models/yolov8n_hero.onnx'
const INPUT_SIZE = 640
const CONF_THRESH = 0.06
const IOU_THRESH = 0.45

let sessionInstance: ort.InferenceSession | null = null
let sessionLoadingPromise: Promise<ort.InferenceSession> | null = null

/**
 * 初始化并确保 ONNX 推理会话单例
 */
export async function ensureSession(): Promise<ort.InferenceSession> {
  if (sessionInstance) {
    return sessionInstance
  }
  if (sessionLoadingPromise) {
    return sessionLoadingPromise
  }

  // 显式配置规范 wasmPaths 与 numThreads=1、proxy=false，规避 relative base URL 异常与 Worker 争用
  const origin = typeof window !== 'undefined' && window.location?.origin ? window.location.origin : ''
  ort.env.wasm.wasmPaths = origin ? `${origin}/ort/` : '/ort/'
  ort.env.wasm.numThreads = 1
  ort.env.wasm.proxy = false

  sessionLoadingPromise = (async () => {
    try {
      console.log('[HeroDetector] 正在加载端侧 ONNX 模型:', MODEL_PATH)
      const session = await ort.InferenceSession.create(MODEL_PATH, {
        executionProviders: ['wasm'],
        graphOptimizationLevel: 'all',
      })
      console.log('[HeroDetector] ONNX 推理会话已就绪，类别数:', YOLO_HERO_CLASSES.length)
      sessionInstance = session
      return session
    } catch (err) {
      console.error('[HeroDetector] 初始化 ONNX 推理会话失败:', err)
      sessionLoadingPromise = null
      throw err
    }
  })()

  return sessionLoadingPromise
}

/**
 * 图像加载辅助：将 Base64 / URL 加载为 HTMLImageElement
 */
function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image()
    img.crossOrigin = 'anonymous'
    img.onload = () => resolve(img)
    img.onerror = err => reject(new Error(`无法加载图像数据: ${err}`))
    img.src = src
  })
}

/**
 * 虚拟全屏 Letterbox 预处理（核心几何对齐算法）
 * 将顶部 18% ROI 投影到 640x640 画布，保持全屏空间位置一致性
 */
async function preprocess(
  imageSource: string | HTMLImageElement,
  screenWidth: number,
  screenHeight: number,
  cropWidth?: number,
  cropHeight?: number,
): Promise<{
  tensor: ort.Tensor
  scale: number
  padX: number
  padY: number
  preprocessDurationMs: number
}> {
  const startTime = performance.now()
  const img = typeof imageSource === 'string' ? await loadImage(imageSource) : imageSource

  const actualCropW = cropWidth || img.naturalWidth || screenWidth
  const actualCropH = cropHeight || img.naturalHeight || Math.round(screenHeight * 0.18)

  // 1. 计算以全屏物理尺寸为基准的等比缩放与居中边距
  const scale = Math.min(INPUT_SIZE / screenWidth, INPUT_SIZE / screenHeight)
  const newFullW = Math.round(screenWidth * scale)
  const padX = Math.round((INPUT_SIZE - newFullW) / 2)
  const padY = Math.round((INPUT_SIZE - Math.round(screenHeight * scale)) / 2)

  // 2. 离屏 Canvas 创建与灰度背景填充 (114, 114, 114)
  const canvas = document.createElement('canvas')
  canvas.width = INPUT_SIZE
  canvas.height = INPUT_SIZE
  const ctx = canvas.getContext('2d', { willReadFrequently: true })
  if (!ctx) {
    throw new Error('无法创建离屏 2D 绘图上下文')
  }

  ctx.fillStyle = 'rgb(114, 114, 114)'
  ctx.fillRect(0, 0, INPUT_SIZE, INPUT_SIZE)

  // 3. 将顶部 18% ROI 画面精确绘制于 (padX, padY) 处
  const targetW = Math.round(actualCropW * scale)
  const targetH = Math.round(actualCropH * scale)
  ctx.drawImage(img, 0, 0, actualCropW, actualCropH, padX, padY, targetW, targetH)

  // 4. 读取 ImageData，转换色彩空间与格式: RGBA -> CHW, 归一化至 [0, 1]
  const imgData = ctx.getImageData(0, 0, INPUT_SIZE, INPUT_SIZE)
  const pixels = imgData.data
  const floatData = new Float32Array(3 * INPUT_SIZE * INPUT_SIZE)
  const channelSize = INPUT_SIZE * INPUT_SIZE

  for (let i = 0; i < channelSize; i++) {
    const px = i * 4
    floatData[i] = pixels[px] / 255.0 // R
    floatData[channelSize + i] = pixels[px + 1] / 255.0 // G
    floatData[2 * channelSize + i] = pixels[px + 2] / 255.0 // B
  }

  const tensor = new ort.Tensor('float32', floatData, [1, 3, INPUT_SIZE, INPUT_SIZE])
  const preprocessDurationMs = Math.round((performance.now() - startTime) * 10) / 10

  return {
    tensor,
    scale,
    padX,
    padY,
    preprocessDurationMs,
  }
}

/**
 * 解析 YOLOv8 输出 Tensor 并将坐标 1:1 反算回全屏物理坐标系
 */
function parseYoloOutput(
  outputTensor: ort.Tensor,
  scale: number,
  padX: number,
  padY: number,
  screenWidth: number,
  screenHeight: number,
): RawDetectionBox[] {
  const dims = outputTensor.dims
  if (!dims || dims.length !== 3) {
    console.warn('[HeroDetector] YOLO 输出 Tensor 维度异常:', dims)
    return []
  }

  const numClasses = dims[1] - 4
  const numDetections = dims[2]
  const data = outputTensor.data as Float32Array

  const boxes: RawDetectionBox[] = []

  for (let i = 0; i < numDetections; i++) {
    const cx_640 = data[i]
    const cy_640 = data[1 * numDetections + i]
    const w_640 = data[2 * numDetections + i]
    const h_640 = data[3 * numDetections + i]

    let maxScore = 0
    let maxCls = 0
    for (let c = 0; c < numClasses; c++) {
      const score = data[(4 + c) * numDetections + i]
      if (score > maxScore) {
        maxScore = score
        maxCls = c
      }
    }

    if (maxScore < CONF_THRESH) {
      continue
    }

    // 从 640x640 Letterbox 坐标反算回真实全屏物理坐标
    const cx = (cx_640 - padX) / scale
    const cy = (cy_640 - padY) / scale
    const bw = w_640 / scale
    const bh = h_640 / scale
    const x1 = cx - bw / 2
    const y1 = cy - bh / 2

    boxes.push({
      classId: maxCls,
      className: YOLO_HERO_CLASSES[maxCls] || `hero_${maxCls}`,
      confidence: maxScore,
      x: Math.max(0, x1),
      y: Math.max(0, y1),
      width: Math.min(bw, screenWidth - x1),
      height: Math.min(bh, screenHeight - y1),
    })
  }

  return boxes
}

/**
 * IoU 交并比计算
 */
function calculateIoU(a: RawDetectionBox, b: RawDetectionBox): number {
  const x1 = Math.max(a.x, b.x)
  const y1 = Math.max(a.y, b.y)
  const x2 = Math.min(a.x + a.width, b.x + b.width)
  const y2 = Math.min(a.y + a.height, b.y + b.height)

  const interWidth = Math.max(0, x2 - x1)
  const interHeight = Math.max(0, y2 - y1)
  const interArea = interWidth * interHeight

  const areaA = a.width * a.height
  const areaB = b.width * b.height
  const unionArea = areaA + areaB - interArea

  return unionArea <= 0 ? 0 : interArea / unionArea
}

/**
 * 非极大值抑制 (NMS)
 */
function applyNms(boxes: RawDetectionBox[]): RawDetectionBox[] {
  const sorted = [...boxes].sort((a, b) => b.confidence - a.confidence)
  const keep: RawDetectionBox[] = []

  for (const box of sorted) {
    let suppressed = false
    for (const kept of keep) {
      if (calculateIoU(box, kept) > IOU_THRESH) {
        suppressed = true
        break
      }
    }
    if (!suppressed) {
      keep.push(box)
    }
  }

  return keep
}

/**
 * 完整端到端英雄视觉识别流水线
 * @param imageSource 截屏 Base64 图像、网络 URL 或已加载的 HTMLImageElement
 * @param screenWidth 屏幕全屏物理宽度 (如 1920)
 * @param screenHeight 屏幕全屏物理高度 (如 1080)
 * @param cropWidth 实际截取的顶部宽度 (通常等于 screenWidth)
 * @param cropHeight 实际截取的顶部高度 (通常约等于 screenHeight * 0.18)
 */
export async function detectBpHeroes(
  imageSource: string | HTMLImageElement,
  screenWidth: number,
  screenHeight: number,
  cropWidth?: number,
  cropHeight?: number,
): Promise<BpDetectionResult> {
  const totalStartTime = performance.now()

  // 1. 确保模型会话就绪
  const session = await ensureSession()

  // 2. 几何 Letterbox 预处理
  const { tensor, scale, padX, padY, preprocessDurationMs } = await preprocess(
    imageSource,
    screenWidth,
    screenHeight,
    cropWidth,
    cropHeight,
  )

  // 3. 执行端侧 ONNX 推理
  const inferStartTime = performance.now()
  const feeds: Record<string, ort.Tensor> = { images: tensor }
  const results = await session.run(feeds)
  const outputTensor = results.output0 || results[Object.keys(results)[0]]
  const inferenceDurationMs = Math.round((performance.now() - inferStartTime) * 10) / 10

  // 4. 解析预测框并反算物理坐标
  const rawBoxes = parseYoloOutput(outputTensor, scale, padX, padY, screenWidth, screenHeight)

  // 5. NMS 过滤去重
  const nmsDetections = applyNms(rawBoxes)

  // 6. 槽位空间匹配判定 (天辉 5 槽位 + 夜魇 5 槽位)
  const slotMatch = matchDetectionsToSlots(nmsDetections, screenWidth, screenHeight)

  const totalDurationMs = Math.round((performance.now() - totalStartTime) * 10) / 10

  return {
    radiant: slotMatch.radiant,
    dire: slotMatch.dire,
    inferenceDurationMs,
    preprocessDurationMs,
    totalDurationMs,
    rawDetectionsCount: nmsDetections.length,
    screenWidth,
    screenHeight,
    timestamp: Date.now(),
  }
}
