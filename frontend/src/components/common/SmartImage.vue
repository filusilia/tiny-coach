<script setup lang="ts">
/**
 * 智能资源图片通用组件 (src/components/common/SmartImage.vue)
 * 职责：
 * 1. 统一收口本地缓存优先与 CDN 平滑降级兜底逻辑
 * 2. 避免在 v-for 循环模板中内联闭包函数
 * 3. 彻底杜绝破图死循环与浏览器控制台红字报错
 */

import { computed } from 'vue'
import { handleImageFallback } from '@/utils/assetUrl.ts'

defineOptions({ name: 'SmartImage' })

const props = withDefaults(
  defineProps<{
    src?: string
    localSrc?: string
    cdnSrc?: string
    alt?: string
    class?: string
    loading?: 'lazy' | 'eager'
    title?: string
    draggable?: boolean
  }>(),
  {
    loading: 'lazy',
    draggable: false,
  },
)

const resolvedSrc = computed(() => props.src || props.localSrc || '')

function onError(e: Event) {
  if (props.cdnSrc) {
    handleImageFallback(e, props.cdnSrc)
  } else {
    handleImageFallback(e, '')
  }
}
</script>

<template>
  <img
    :src="resolvedSrc"
    :alt="alt || ''"
    :title="title"
    :class="props.class"
    :loading="loading"
    :draggable="draggable"
    @error="onError"
  >
</template>
