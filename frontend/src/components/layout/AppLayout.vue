<template>
  <div class="min-h-screen bg-muted/20">
    <NavBar />
    <main class="min-h-[calc(100vh-3.5rem)]">
      <slot />
    </main>
  </div>
</template>

<script setup lang="ts">
import NavBar from './NavBar.vue'
</script>
