<script setup lang="ts">
import type { PropType } from 'vue'
import { CheckCircle2, Loader2 } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Panel } from '@/components/ui/panel'
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'

interface Challenge {
  id: string
  title: string
}

defineProps({
  challenges: {
    type: Array as PropType<Challenge[]>,
    default: () => [],
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
  statusMessage: {
    type: String,
    default: null,
  },
})

const emit = defineEmits<{
  'update:selectedChallenge': [value: string]
  'update:flagInput': [value: string]
  'submit': []
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
        <p v-if="statusMessage" class="text-sm text-muted-foreground">
          {{ statusMessage }}
        </p>
      </div>
    </CardContent>
  </Panel>
</template>
