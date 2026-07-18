<script setup lang="ts">
import { Edit3, Loader2, Plus, RefreshCw, Trash2 } from 'lucide-vue-next'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { normalizeDirection } from '@/lib/challengeDirections'

interface CompetitionChallengeCard {
  id: string
  title: string
  typeId: string
  direction: string
  deploymentType?: string | number
  pointsConfig: {
    initialPoints: number
    minimumPoints: number
  }
  hints: Array<{ content: string }>
}

const props = defineProps<{
  competitionId: string
  challenges?: CompetitionChallengeCard[]
  loading: boolean
  deleting: boolean
  restarting: boolean
}>()

const emit = defineEmits<{
  delete: [challengeId: string]
  restart: [challengeId: string]
}>()

const { t } = useI18n()
const allDirections = '__all__'
const direction = ref(allDirections)

const directions = computed(() => [...new Set((props.challenges ?? []).map(challenge => normalizeDirection(challenge.direction)))].sort())
const filteredChallenges = computed(() => direction.value === allDirections
  ? props.challenges ?? []
  : (props.challenges ?? []).filter(challenge => normalizeDirection(challenge.direction) === direction.value))

function isStaticContainer(challenge: CompetitionChallengeCard) {
  return challenge.deploymentType === 'StaticContainer' || challenge.deploymentType === 3
}
</script>

<template>
  <section class="space-y-5">
    <div class="flex flex-col gap-4 border-b pb-5 sm:flex-row sm:items-end sm:justify-between">
      <div>
        <h3 class="text-lg font-semibold">{{ t('admin.competitionDetail.competitionChallenges') }}</h3>
        <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.challengeCardsDescription') }}</p>
      </div>
      <Button as-child>
        <RouterLink :to="{ name: 'admin-competition-challenge-create', params: { id: competitionId } }">
          <Plus class="mr-2 size-4" />
          {{ t('admin.competitionDetail.createCompetitionChallenge') }}
        </RouterLink>
      </Button>
    </div>

    <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <Select v-model="direction">
        <SelectTrigger class="w-full sm:w-64">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem :value="allDirections">{{ t('admin.challenges.allDirections') }}</SelectItem>
          <SelectItem v-for="item in directions" :key="item" :value="item">{{ item }}</SelectItem>
        </SelectContent>
      </Select>
      <p v-if="!loading" class="text-xs text-muted-foreground">
        {{ t('admin.competitionDetail.challengeResultCount', { count: filteredChallenges.length }) }}
      </p>
    </div>

    <div v-if="loading" class="flex min-h-56 items-center justify-center text-sm text-muted-foreground">
      <Loader2 class="mr-2 size-4 animate-spin" />
      {{ t('common.loading') }}
    </div>

    <div v-else-if="filteredChallenges.length" class="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      <Card v-for="challenge in filteredChallenges" :key="challenge.id" class="group p-0">
        <CardContent class="flex h-full min-h-52 flex-col p-5">
          <div class="flex items-start justify-between gap-3">
            <div class="min-w-0">
              <h4 class="truncate font-semibold" :title="challenge.title">{{ challenge.title }}</h4>
              <p class="mt-1 text-xs text-muted-foreground">{{ challenge.typeId }}</p>
            </div>
            <Badge variant="outline">{{ normalizeDirection(challenge.direction) }}</Badge>
          </div>

          <dl class="mt-6 grid grid-cols-2 gap-3 text-sm">
            <div class="border-l-2 border-primary/30 pl-3">
              <dt class="text-xs text-muted-foreground">{{ t('admin.competitionDetail.scoreRange') }}</dt>
              <dd class="mt-1 font-mono">{{ challenge.pointsConfig.minimumPoints }} → {{ challenge.pointsConfig.initialPoints }}</dd>
            </div>
            <div class="border-l-2 border-primary/30 pl-3">
              <dt class="text-xs text-muted-foreground">{{ t('admin.competitionDetail.hints') }}</dt>
              <dd class="mt-1 font-mono">{{ challenge.hints?.length ?? 0 }}</dd>
            </div>
          </dl>

          <div class="mt-auto flex items-center justify-end gap-2 border-t pt-4">
            <Button
              v-if="isStaticContainer(challenge)"
              variant="outline"
              size="icon"
              :disabled="restarting"
              :title="t('admin.competitionDetail.restartContainer')"
              @click="emit('restart', challenge.id)"
            >
              <Loader2 v-if="restarting" class="size-4 animate-spin" />
              <RefreshCw v-else class="size-4" />
            </Button>
            <Button variant="outline" size="sm" as-child>
              <RouterLink :to="{ name: 'admin-competition-challenge-edit', params: { id: competitionId, challengeId: challenge.id } }">
                <Edit3 class="mr-2 size-4" />
                {{ t('common.edit') }}
              </RouterLink>
            </Button>
            <Button
              variant="ghost"
              size="icon"
              class="text-destructive"
              :disabled="deleting"
              :title="t('common.delete')"
              @click="emit('delete', challenge.id)"
            >
              <Trash2 class="size-4" />
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>

    <Card v-else class="flex min-h-56 flex-col items-center justify-center border-dashed p-8 text-center">
      <p class="font-medium">{{ t('admin.competitionDetail.noDeployedChallenges') }}</p>
      <p class="mt-1 text-sm text-muted-foreground">{{ t('admin.competitionDetail.noChallengeFilterResults') }}</p>
    </Card>
  </section>
</template>
