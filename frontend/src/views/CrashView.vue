<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { ArrowLeft, Clipboard, Home, RotateCcw, ShieldAlert } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { Button } from '@/components/ui/button'
import type { AppErrorDetails } from '@/types/app-error'

const props = defineProps<{
  error?: AppErrorDetails | null
}>()

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

const isDev = import.meta.env.DEV
const referenceId = computed(() => props.error?.id ?? t('pages.crash.manualReference'))
const currentPath = computed(() => props.error?.path ?? route.fullPath)
const occurredAt = computed(() => props.error?.time ?? new Date().toISOString())
const hasTechnicalDetails = computed(
  () => Boolean(props.error?.message) || Boolean(props.error?.stack) || Boolean(props.error?.info),
)
const detailText = computed(() =>
  JSON.stringify(
    {
      referenceId: referenceId.value,
      path: currentPath.value,
      time: occurredAt.value,
      source: props.error?.source ?? 'manual',
      info: props.error?.info,
      message: props.error?.message,
      stack: props.error?.stack,
    },
    null,
    2,
  ),
)

function reloadPage() {
  window.location.reload()
}

function goBack() {
  if (window.history.length > 1) {
    router.back()
    return
  }

  router.push('/competitions')
}

async function copyDetails() {
  try {
    await navigator.clipboard.writeText(detailText.value)
    toast.success(t('pages.crash.copySuccess'))
  } catch {
    toast.error(t('pages.crash.copyError'))
  }
}
</script>

<template>
  <main class="min-h-svh bg-background">
    <div class="mx-auto flex min-h-svh w-full max-w-[980px] items-center px-4 py-8 md:px-6">
      <section class="noctf-workbench w-full divide-y divide-border/90">
        <div class="grid gap-5 p-5 md:grid-cols-[9rem_minmax(0,1fr)] md:p-6">
          <div class="min-w-0">
            <p class="noctf-label">{{ t('pages.crash.kicker') }}</p>
            <p class="mt-3 font-mono text-5xl font-semibold leading-none text-danger">500</p>
          </div>

          <div class="min-w-0 space-y-3">
            <div class="flex items-center gap-2">
              <ShieldAlert class="size-5 shrink-0 text-danger" />
              <h1 class="text-xl font-semibold tracking-normal">{{ t('pages.crash.title') }}</h1>
            </div>
            <p class="max-w-2xl text-sm leading-6 text-muted-foreground">
              {{ t('pages.crash.description') }}
            </p>
          </div>
        </div>

        <div
          class="grid divide-y divide-border/90 text-sm md:grid-cols-3 md:divide-x md:divide-y-0"
        >
          <div class="min-w-0 p-4">
            <p class="noctf-label">{{ t('pages.crash.pathLabel') }}</p>
            <p class="mt-2 truncate font-mono text-xs text-foreground">{{ currentPath }}</p>
          </div>
          <div class="min-w-0 p-4">
            <p class="noctf-label">{{ t('pages.crash.referenceLabel') }}</p>
            <p class="mt-2 truncate font-mono text-xs text-foreground">{{ referenceId }}</p>
          </div>
          <div class="min-w-0 p-4">
            <p class="noctf-label">{{ t('pages.crash.reportLabel') }}</p>
            <p class="mt-2 text-muted-foreground">{{ t('pages.crash.reportReserved') }}</p>
          </div>
        </div>

        <div class="flex flex-col gap-2 p-4 sm:flex-row sm:items-center">
          <Button type="button" @click="reloadPage">
            <RotateCcw class="size-4" />
            {{ t('pages.crash.reload') }}
          </Button>
          <Button type="button" variant="outline" @click="goBack">
            <ArrowLeft class="size-4" />
            {{ t('nav.back') }}
          </Button>
          <Button type="button" variant="outline" as-child>
            <RouterLink to="/competitions">
              <Home class="size-4" />
              {{ t('nav.backToCompetitions') }}
            </RouterLink>
          </Button>
          <Button type="button" variant="ghost" class="sm:ml-auto" @click="copyDetails">
            <Clipboard class="size-4" />
            {{ t('pages.crash.copyDetails') }}
          </Button>
        </div>

        <details v-if="isDev && hasTechnicalDetails" class="p-4">
          <summary class="cursor-pointer text-sm font-semibold text-foreground">
            {{ t('pages.crash.technicalDetails') }}
          </summary>
          <pre
            class="noctf-terminal noctf-scrollbar mt-3 max-h-72 overflow-auto p-3 text-xs leading-5"
            >{{ detailText }}</pre>
        </details>
      </section>
    </div>
  </main>
</template>
