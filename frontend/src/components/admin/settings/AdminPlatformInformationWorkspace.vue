<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { Box, RefreshCw, Users } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { platformAdminApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'

const { t } = useI18n()
const {
  data: information,
  isError,
  isLoading,
  refetch,
} = useQuery({
  queryKey: queryKeys.adminPlatformInformation,
  queryFn: platformAdminApi.platformInformation,
  staleTime: 10 * 60 * 1000,
})
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col justify-between gap-4 sm:flex-row sm:items-start">
      <div class="space-y-1">
        <div class="flex items-center gap-2">
          <h2 class="text-2xl font-bold tracking-tight">
            {{ t('admin.settings.information.title') }}
          </h2>
          <Badge variant="outline" class="rounded-none font-mono">
            {{ information?.version || '—' }}
          </Badge>
        </div>
        <p class="max-w-2xl text-sm text-muted-foreground">
          {{ t('admin.settings.information.subtitle') }}
        </p>
      </div>
      <Button variant="outline" :disabled="isLoading" @click="refetch()">
        <RefreshCw class="size-4" :class="{ 'animate-spin': isLoading }" />
        {{ t('common.refresh') }}
      </Button>
    </div>

    <div v-if="isLoading" class="grid gap-4 md:grid-cols-2">
      <Skeleton class="h-40 rounded-none" />
      <Skeleton class="h-40 rounded-none" />
    </div>

    <Alert v-else-if="isError" variant="destructive" class="rounded-none border-2">
      <AlertTitle>{{ t('state.failedToLoad') }}</AlertTitle>
      <AlertDescription class="mt-3">
        <Button variant="outline" size="sm" @click="refetch()">
          {{ t('common.retry') }}
        </Button>
      </AlertDescription>
    </Alert>

    <div v-else class="grid gap-5 lg:grid-cols-[18rem_minmax(0,1fr)]">
      <Card class="rounded-none border-2 shadow-none">
        <CardHeader class="border-b-2 border-border">
          <CardTitle class="flex items-center gap-2">
            <Box class="size-5" />
            {{ t('admin.settings.information.version') }}
          </CardTitle>
        </CardHeader>
        <CardContent class="pt-6">
          <p class="break-all font-mono text-2xl font-black tracking-tight">
            {{ information?.version || '—' }}
          </p>
          <p class="mt-3 text-xs leading-5 text-muted-foreground">
            {{ t('admin.settings.information.versionDescription') }}
          </p>
        </CardContent>
      </Card>

      <Card class="rounded-none border-2 shadow-none">
        <CardHeader class="border-b-2 border-border">
          <CardTitle class="flex items-center gap-2">
            <Users class="size-5" />
            {{ t('admin.settings.information.contributors') }}
          </CardTitle>
        </CardHeader>
        <CardContent class="pt-6">
          <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            <div
              v-for="contributor in information?.contributors || []"
              :key="contributor.id"
              class="flex items-center gap-3 border-2 bg-muted/20 p-3"
            >
              <img
                :src="contributor.avatarUrl"
                :alt="contributor.id"
                class="size-11 shrink-0 rounded-full border-2 border-border object-cover"
              >
              <span class="min-w-0 truncate font-mono text-sm font-bold">
                {{ contributor.id }}
              </span>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </div>
</template>
