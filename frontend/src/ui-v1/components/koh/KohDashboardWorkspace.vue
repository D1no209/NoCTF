<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import PageHeader from '@/ui-v1/components/layout/PageHeader.vue'
import KohStatusCard from '@/ui-v1/components/game/KohStatusCard.vue'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Skeleton } from '@/ui-v1/components/ui/skeleton'
import { Button } from '@/ui-v1/components/ui/button'
import { Card } from '@/ui-v1/components/ui/card'
import { vAutoAnimate } from '@formkit/auto-animate/vue'
import { Trophy, ShieldAlert } from 'lucide-vue-next'
import { useKohDashboardPage } from '@/features/competitions/useKohDashboardPage'

const { t } = useI18n()

const {
  challenges,
  isLoading,
  isError,
  refetch,
} = useKohDashboardPage()
</script>

<template>
  <div class="mx-auto w-full max-w-[1600px] space-y-6 px-4 py-6 md:px-6 lg:px-8">
    <div class="flex flex-col md:flex-row md:items-end justify-between gap-4">
      <PageHeader
        :title="t('koh.kingOfTheHill')"
        :description="t('koh.subtitle')"
      >
        <template #actions>
          <div class="flex items-center gap-2">
            <Badge variant="secondary" class="animate-pulse border-[var(--semantic-warning-border)] bg-[var(--semantic-warning-soft)] text-[var(--semantic-warning)] px-3">
              <Trophy class="mr-1.5 size-3.5" />
              {{ t('common.live') }}
            </Badge>
            <Badge variant="outline" class="font-mono text-[10px] uppercase tracking-widest bg-muted/30">
              {{ t('common.mode') }}: KOH
            </Badge>
          </div>
        </template>
      </PageHeader>
    </div>

    <!-- Content Area -->
    <div v-if="isLoading" class="grid gap-6 grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
      <Skeleton v-for="i in 4" :key="i" class="h-64 rounded-xl" />
    </div>

    <Card v-else-if="isError" class="flex min-h-40 flex-col items-center justify-center border-dashed py-20 text-center">
      <div class="size-16 rounded-full bg-destructive/10 flex items-center justify-center text-destructive mb-4">
        <ShieldAlert class="size-8" />
      </div>
      <h3 class="text-xl font-bold">{{ t('koh.loadError') }}</h3>
      <p class="text-muted-foreground mt-2 max-w-xs">{{ t('koh.loadErrorDetail') }}</p>
      <Button variant="outline" class="mt-6" @click="refetch">{{ t('common.refresh') }}</Button>
    </Card>

    <Card v-else-if="challenges.length === 0" class="flex min-h-40 flex-col items-center justify-center border-dashed py-20 text-center">
      <div class="size-16 rounded-full bg-muted flex items-center justify-center text-muted-foreground mb-4">
        <Trophy class="size-8" />
      </div>
      <h3 class="text-xl font-bold">{{ t('koh.empty') }}</h3>
      <p class="text-muted-foreground mt-2 max-w-xs">{{ t('koh.emptyDetail') }}</p>
    </Card>

    <div 
      v-else 
      v-auto-animate
      class="grid gap-6"
      :class="[
        challenges.length === 1 ? 'grid-cols-1 max-w-xl mx-auto' :
        challenges.length === 2 ? 'grid-cols-1 md:grid-cols-2 max-w-4xl mx-auto' :
        'grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4'
      ]"
    >
      <KohStatusCard
        v-for="ch in challenges"
        :key="ch.challengeId"
        :status="ch"
        class="transition-all duration-200 hover:-translate-y-0.5 hover:shadow-float"
      />
    </div>
  </div>
</template>
