<script setup lang="ts">
import NavBar from './NavBar.vue'
import { useRoute } from 'vue-router'

const route = useRoute()
</script>

<template>
  <div class="flex min-h-[100dvh] flex-col bg-background">
    <NavBar />
    <main class="flex-1 flex flex-col">
      <slot>
        <router-view v-slot="{ Component }">
          <transition name="fade" mode="out-in">
            <component :is="Component" :key="route.fullPath" />
          </transition>
        </router-view>
      </slot>
    </main>
  </div>
</template>

<style scoped>
.fade-enter-active,
.fade-leave-active {
  transition:
    opacity var(--motion-fast) var(--ease-out-quint),
    transform var(--motion-fast) var(--ease-out-quint);
}

.fade-enter-from {
  opacity: 0;
  transform: translateY(4px);
}

.fade-leave-to {
  opacity: 0;
  transform: translateY(-2px);
}
</style>
