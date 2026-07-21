<script setup lang="ts">
import type { PropType } from 'vue'
import { CheckCircle2, Loader2 } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Button } from '@/ui-v1/components/ui/button'
import { CardContent, CardHeader, CardTitle } from '@/ui-v1/components/ui/card'
import { Panel } from '@/ui-v1/components/ui/panel'
import { Input } from '@/ui-v1/components/ui/input'
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/ui-v1/components/ui/select'

interface Team {
  id: string
  name: string
}

interface Challenge {
  id: string
  title: string
}

defineProps({
  teams: {
    type: Array as PropType<Team[]>,
    default: () => [],
  },
  challenges: {
    type: Array as PropType<Challenge[]>,
    default: () => [],
  },
  selectedVictim: {
    type: String,
    required: true,
  },
  selectedChallenge: {
    type: String,
    required: true,
  },
  flagInput: {
    type: String,
    required: true,
  },
  loading: {
    type: Boolean,
    default: false,
  },
})

const emit = defineEmits<{
  'update:selectedVictim': [value: string]
  'update:selectedChallenge': [value: string]
  'update:flagInput': [value: string]
  submit: []
}>()

const { t } = useI18n()
</script>

<template>
  <Panel class="border-primary/10">
    <CardHeader>
      <CardTitle class="flex items-center gap-2">
        <CheckCircle2 class="size-5 text-primary" />
        {{ t('awd.submitFlag') }}
      </CardTitle>
    </CardHeader>
    <CardContent class="space-y-4">
      <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div class="space-y-2">
          <label>{{ t('awd.victimTeam') }}</label>
          <Select :model-value="selectedVictim" @update:model-value="emit('update:selectedVictim', String($event))">
            <SelectTrigger>
              <SelectValue :placeholder="t('awd.selectTeam')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="team in teams" :key="team.id" :value="team.id">
                  {{ team.name }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </div>

        <div class="space-y-2">
          <label>{{ t('common.challenge') }}</label>
          <Select :model-value="selectedChallenge" @update:model-value="emit('update:selectedChallenge', String($event))">
            <SelectTrigger>
              <SelectValue :placeholder="t('awd.selectChallenge')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="challenge in challenges" :key="challenge.id" :value="challenge.id">
                  {{ challenge.title }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </div>
      </div>

      <div class="space-y-2">
        <label>{{ t('awd.flag') }}</label>
        <div class="flex gap-2">
          <Input
            :model-value="flagInput"
            :placeholder="t('challenges.flagPlaceholder')"
            class="font-mono"
            :disabled="loading"
            @update:model-value="emit('update:flagInput', String($event))"
            @keydown.enter="emit('submit')"
          />
          <Button
            class="shrink-0"
            :disabled="loading || !flagInput.trim() || !selectedChallenge"
            @click="emit('submit')"
          >
            <Loader2 v-if="loading" class="mr-2 size-4 animate-spin" />
            {{ t('awd.submitFlag') }}
          </Button>
        </div>
      </div>
    </CardContent>
  </Panel>
</template>