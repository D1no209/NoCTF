<script setup lang="ts">
import { useRoute } from 'vue-router'
import { computed } from 'vue'
import NavBar from '@/components/layout/NavBar.vue'

const route = useRoute()
const showNavBar = computed(() => route.meta.requiresAuth !== false)
</script>

<template>
  <div class="min-h-screen flex flex-col">
    <NavBar v-if="showNavBar" />
    <main class="flex-1">
      <RouterView />
    </main>
  </div>
</template>
