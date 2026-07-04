<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { ArrowLeft, Compass, Home, Shield } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const currentPath = computed(() => route.fullPath)
const canManage = computed(() => ['Admin', 'Organizer'].includes(auth.userRole))

function goBack() {
  if (window.history.length > 1) {
    router.back()
    return
  }

  router.push('/competitions')
}
</script>

<template>
  <main class="min-h-svh bg-background">
    <div class="mx-auto flex min-h-svh w-full max-w-[980px] items-center px-4 py-8 md:px-6">
      <section class="noctf-workbench w-full divide-y divide-border/90">
        <div class="grid gap-5 p-5 md:grid-cols-[9rem_minmax(0,1fr)] md:p-6">
          <div class="min-w-0">
            <p class="noctf-label">{{ t('pages.notFound.kicker') }}</p>
            <p class="mt-3 font-mono text-5xl font-semibold leading-none text-foreground">404</p>
          </div>

          <div class="min-w-0 space-y-3">
            <div class="flex items-center gap-2">
              <Compass class="size-5 shrink-0 text-primary" />
              <h1 class="text-xl font-semibold tracking-normal">{{ t('pages.notFound.title') }}</h1>
            </div>
            <p class="max-w-2xl text-sm leading-6 text-muted-foreground">
              {{ t('pages.notFound.description') }}
            </p>
          </div>
        </div>

        <div
          class="grid divide-y divide-border/90 text-sm md:grid-cols-[1fr_auto] md:divide-x md:divide-y-0"
        >
          <div class="min-w-0 p-4">
            <p class="noctf-label">{{ t('pages.notFound.pathLabel') }}</p>
            <p class="mt-2 truncate font-mono text-xs text-foreground">{{ currentPath }}</p>
          </div>
          <div class="min-w-0 p-4 md:min-w-52">
            <p class="noctf-label">{{ t('common.status') }}</p>
            <p class="mt-2 text-muted-foreground">{{ t('pages.notFound.status') }}</p>
          </div>
        </div>

        <div class="flex flex-col gap-2 p-4 sm:flex-row sm:items-center">
          <Button type="button" variant="outline" @click="goBack">
            <ArrowLeft class="size-4" />
            {{ t('nav.back') }}
          </Button>
          <Button type="button" as-child>
            <RouterLink to="/competitions">
              <Home class="size-4" />
              {{ t('nav.backToCompetitions') }}
            </RouterLink>
          </Button>
          <Button v-if="canManage" type="button" variant="secondary" as-child>
            <RouterLink to="/admin">
              <Shield class="size-4" />
              {{ t('nav.admin') }}
            </RouterLink>
          </Button>
        </div>
      </section>
    </div>
  </main>
</template>
