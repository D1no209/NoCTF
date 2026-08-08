<script setup lang="ts">
const { configuration } = usePlatform()
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-8 px-4 py-12">
    <section class="flex flex-col gap-4">
      <h1 class="text-4xl font-bold">{{ configuration?.name ?? 'NoCTF' }}</h1>
      <p v-if="configuration?.description" class="max-w-2xl text-lg text-muted-foreground">
        {{ configuration.description }}
      </p>
      <div class="flex gap-2">
        <Button as-child>
          <NuxtLink to="/competitions">浏览竞赛</NuxtLink>
        </Button>
        <Button variant="outline" as-child>
          <NuxtLink to="/auth/register">立即注册</NuxtLink>
        </Button>
      </div>
    </section>
  </div>
</template>
