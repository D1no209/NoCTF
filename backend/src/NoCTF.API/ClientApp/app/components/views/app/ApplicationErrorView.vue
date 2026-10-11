<script setup lang="ts">
import { toRefs } from 'vue'
import type { ApplicationErrorViewState } from '~/features/shell/useApplicationError'

const props = defineProps<{ state: ApplicationErrorViewState }>()
const { configuration, t, statusCode, title, description, displayPath, previousPath, recovering, navigationFailed, primaryLabel, secondaryLabel, PrimaryIcon, SecondaryIcon, ArrowLeft, LanguageToggle, ThemeToggle, primaryAction, secondaryAction, goHome, goBack } = toRefs(props.state)
</script>

<template>
  <TooltipProvider :delay-duration="350">
    <div data-slot="application-error" class="relative isolate flex h-dvh min-h-0 flex-col overflow-hidden">
      <div data-page-wallpaper="true" aria-hidden="true" />
      <header class="shrink-0">
        <div class="mx-auto flex h-20 w-full max-w-[96rem] items-center justify-between gap-3 px-4 md:px-6">
          <div data-slot="topbar-capsule" data-position="left">
            <Button variant="ghost" :disabled="recovering" :aria-label="t('errorPage.action.home')" @click="goHome">
              <img v-if="configuration?.logoUrl" :src="configuration.logoUrl" alt="" class="size-8 shrink-0 rounded-full object-contain" decoding="async">
              <span v-else aria-hidden="true" class="text-primary">&gt;_</span>
              <span class="max-w-48 truncate font-semibold">{{ configuration?.name ?? t('common.label.noctf') }}</span>
            </Button>
          </div>
          <div data-slot="topbar-capsule" data-position="right">
            <component :is="LanguageToggle" />
            <component :is="ThemeToggle" />
          </div>
        </div>
      </header>

      <ScrollSurface as="main" axis="y" class="min-h-0 flex-1" aria-labelledby="application-error-title">
        <div class="mx-auto flex min-h-full w-full max-w-5xl items-center px-4 py-8 md:px-6 md:py-12">
          <Card slot-name="application-error-card" class="w-full gap-0 py-0 md:grid md:grid-cols-[0.8fr_1.2fr]">
            <div class="flex items-center justify-center px-6 pt-10 pb-6 md:py-16" aria-hidden="true">
              <span data-slot="application-error-code" class="font-mono text-display tabular-nums text-primary">{{ statusCode }}</span>
            </div>
            <div class="flex min-w-0 flex-col gap-6 pb-8 md:justify-center md:py-14 md:pr-10">
              <CardHeader class="gap-4 px-6">
                <span class="sr-only">{{ t('errorPage.status', { code: statusCode }) }}</span>
                <CardTitle><h1 id="application-error-title" class="text-display text-2xl md:text-3xl">{{ title }}</h1></CardTitle>
                <CardDescription><p class="text-base leading-relaxed">{{ description }}</p></CardDescription>
              </CardHeader>
              <CardContent class="flex min-w-0 flex-col gap-6 px-6">
                <div v-if="displayPath" class="flex min-w-0 flex-col gap-2">
                  <span class="text-sm text-muted-foreground">{{ t('errorPage.path') }}</span>
                  <code class="font-mono text-sm break-all">{{ displayPath }}</code>
                </div>
                <div class="flex flex-col gap-3 sm:flex-row sm:flex-wrap" :aria-busy="recovering || undefined">
                  <Button class="min-h-11" :disabled="recovering" @click="primaryAction">
                    <Spinner v-if="recovering" data-icon="inline-start" />
                    <component v-else :is="PrimaryIcon" data-icon="inline-start" aria-hidden="true" />
                    {{ primaryLabel }}
                  </Button>
                  <Button variant="secondary" class="min-h-11" :disabled="recovering" @click="secondaryAction">
                    <component :is="SecondaryIcon" data-icon="inline-start" aria-hidden="true" />
                    {{ secondaryLabel }}
                  </Button>
                </div>
                <Alert v-if="navigationFailed" variant="destructive">
                  <AlertDescription>{{ t('errorPage.navigationFailed') }}</AlertDescription>
                </Alert>
              </CardContent>
              <CardFooter v-if="previousPath" class="px-6 py-0">
                <Button variant="ghost" :disabled="recovering" @click="goBack">
                  <component :is="ArrowLeft" data-icon="inline-start" aria-hidden="true" />
                  {{ t('errorPage.action.back') }}
                </Button>
              </CardFooter>
            </div>
          </Card>
        </div>
      </ScrollSurface>
    </div>
  </TooltipProvider>
</template>
