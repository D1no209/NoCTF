<script setup lang="ts">
import { Save } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Panel } from '@/components/ui/panel'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'

defineProps<{
  competitionForm: any
  saving: boolean
}>()

const emit = defineEmits<{
  save: []
}>()

const { t } = useI18n()
</script>

<template>
  <Card class="p-4">
    <div class="mb-5 flex items-center justify-between gap-4">
      <div>
        <h3 class="font-semibold">
          {{ t('admin.competitionDetail.settingsTitle') }}
        </h3>
        <p class="text-sm text-muted-foreground">
          {{ t('admin.competitionDetail.settingsDescription') }}
        </p>
      </div>
      <Button :disabled="saving" @click="emit('save')">
        <Save class="mr-2 size-4" />
        {{ t('common.save') }}
      </Button>
    </div>

    <div class="grid gap-4 xl:grid-cols-2">
      <Card class="p-0">
        <CardContent class="space-y-4 p-4">
          <div class="border-b pb-3">
            <h4 class="font-semibold">{{ t('admin.competitionDetail.identitySection') }}</h4>
            <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.identitySectionDescription') }}</p>
          </div>
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.titleColumn') }}</Label>
              <Input v-model="competitionForm.title" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.status') }}</Label>
              <Select v-model="competitionForm.status">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="Draft">{{ t('competitions.status.draft') }}</SelectItem>
                  <SelectItem value="Published">{{ t('competitions.status.published') }}</SelectItem>
                  <SelectItem value="Running">{{ t('competitions.status.running') }}</SelectItem>
                  <SelectItem value="Paused">{{ t('competitions.status.paused') }}</SelectItem>
                  <SelectItem value="Finished">{{ t('competitions.status.finished') }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2 sm:col-span-2">
              <Label>{{ t('admin.competitions.description') }}</Label>
              <Textarea v-model="competitionForm.description" rows="5" />
            </div>
          </div>
        </CardContent>
      </Card>

      <Card class="p-0">
        <CardContent class="space-y-4 p-4">
          <div class="border-b pb-3">
            <h4 class="font-semibold">{{ t('admin.competitionDetail.formatSection') }}</h4>
            <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.formatSectionDescription') }}</p>
          </div>
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.gameMode') }}</Label>
              <Select v-model="competitionForm.gameModeType">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="Ctf">CTF</SelectItem>
                  <SelectItem value="Awd">AWD</SelectItem>
                  <SelectItem value="Awdp">AWDP</SelectItem>
                  <SelectItem value="Koh">KoH</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.difficultyCoefficient') }}</Label>
              <Input v-model.number="competitionForm.difficultyCoefficient" type="number" min="0.1" step="0.1" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitionDetail.maxTeamMembers') }}</Label>
              <Input v-model.number="competitionForm.maxTeamMembers" type="number" min="1" />
            </div>
            <div class="grid gap-2">
              <Label for="max-concurrent-runtime-instances-per-team">
                {{ t('admin.competitionDetail.maxConcurrentRuntimeInstancesPerTeam') }}
              </Label>
              <Input
                id="max-concurrent-runtime-instances-per-team"
                v-model.number="competitionForm.maxConcurrentRuntimeInstancesPerTeam"
                type="number"
                min="0"
                step="1"
                aria-describedby="max-concurrent-runtime-instances-per-team-hint"
              />
              <p
                id="max-concurrent-runtime-instances-per-team-hint"
                class="text-xs text-muted-foreground"
              >
                {{ t('admin.competitionDetail.maxConcurrentRuntimeInstancesPerTeamHint') }}
              </p>
            </div>
            <Panel>
              <label class="flex min-h-10 cursor-pointer items-center gap-3 px-3 py-2 text-sm">
                <input v-model="competitionForm.teamRegistrationAutoApprove" type="checkbox" class="size-4">
                <span>{{ t('admin.competitionDetail.autoApproveTeams') }}</span>
              </label>
            </Panel>
            <Panel class="sm:col-span-2">
              <label class="flex min-h-10 cursor-pointer items-center gap-3 px-3 py-2 text-sm">
                <input v-model="competitionForm.tracksEnabled" type="checkbox" class="size-4">
                <span>{{ t('admin.competitionDetail.enableTracks') }}</span>
              </label>
            </Panel>
            <div v-if="competitionForm.tracksEnabled" class="grid gap-2 sm:col-span-2">
              <Label>{{ t('admin.competitionDetail.trackNames') }}</Label>
              <Textarea v-model="competitionForm.trackNamesText" rows="3" :placeholder="t('admin.competitionDetail.trackNamesPlaceholder')" />
            </div>
          </div>
        </CardContent>
      </Card>

      <Card class="p-0 xl:col-span-2">
        <CardContent class="space-y-4 p-4">
          <div class="border-b pb-3">
            <h4 class="font-semibold">{{ t('admin.competitionDetail.scheduleSection') }}</h4>
            <p class="text-sm text-muted-foreground">{{ t('admin.competitionDetail.scheduleSectionDescription') }}</p>
          </div>
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.startTime') }}</Label>
              <Input v-model="competitionForm.startTime" type="datetime-local" />
            </div>
            <div class="grid gap-2">
              <Label>{{ t('admin.competitions.endTime') }}</Label>
              <Input v-model="competitionForm.endTime" type="datetime-local" />
            </div>
          </div>
        </CardContent>
      </Card>
    </div>

    <Card v-if="competitionForm.gameModeType === 'Ctf'" class="mt-4 p-0">
      <CardContent class="grid gap-4 lg:grid-cols-3 p-4">
        <div class="grid gap-2">
          <Label>{{ t('admin.competitionDetail.firstBloodBonus') }}</Label>
          <Input v-model.number="competitionForm.firstBloodBonusPercent" type="number" min="0" step="1" />
        </div>
        <div class="grid gap-2">
          <Label>{{ t('admin.competitionDetail.secondBloodBonus') }}</Label>
          <Input v-model.number="competitionForm.secondBloodBonusPercent" type="number" min="0" step="1" />
        </div>
        <div class="grid gap-2">
          <Label>{{ t('admin.competitionDetail.thirdBloodBonus') }}</Label>
          <Input v-model.number="competitionForm.thirdBloodBonusPercent" type="number" min="0" step="1" />
        </div>
      </CardContent>
    </Card>

    <Card v-if="competitionForm.gameModeType === 'Awd' || competitionForm.gameModeType === 'Awdp'" class="mt-4 p-0">
      <CardContent class="grid gap-4 lg:grid-cols-4 p-4">
        <div class="grid gap-2">
          <Label>{{ t('admin.competitionDetail.roundDurationSeconds') }}</Label>
          <Input v-model.number="competitionForm.roundDurationSeconds" type="number" min="1" placeholder="300" />
        </div>
        <div class="grid gap-2">
          <Label>{{ t('admin.competitionDetail.totalRounds') }}</Label>
          <Input v-model.number="competitionForm.totalRounds" type="number" min="1" placeholder="10" />
        </div>
        <template v-if="competitionForm.gameModeType === 'Awd'">
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdAttackPoints') }}</Label><Input v-model.number="competitionForm.attackPoints" type="number" min="0" placeholder="50" /></div>
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdDefensePoints') }}</Label><Input v-model.number="competitionForm.serviceOnlinePoints" type="number" min="0" placeholder="100" /></div>
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdServiceDownPenalty') }}</Label><Input v-model.number="competitionForm.serviceDownPenalty" type="number" min="0" placeholder="50" /></div>
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdBeenAttackedPenalty') }}</Label><Input v-model.number="competitionForm.beenAttackedPenalty" type="number" min="0" placeholder="50" /></div>
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.flagValidityRounds') }}</Label><Input v-model.number="competitionForm.flagValidityRounds" type="number" min="1" placeholder="2" /></div>
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.flagFormat') }}</Label><Input v-model="competitionForm.flagFormat" placeholder="flag{{{0}}}" /></div>
          <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.flagPath') }}</Label><Input v-model="competitionForm.flagPath" placeholder="/flag/flag.txt" /></div>
        </template>
      </CardContent>
    </Card>

    <Card v-if="competitionForm.gameModeType === 'Awdp'" class="mt-4 p-0">
      <CardContent class="grid gap-4 lg:grid-cols-3 p-4">
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpAttackScore') }}</Label><Input v-model.number="competitionForm.awdpAttackScorePerRound" type="number" min="0" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpDefenseScore') }}</Label><Input v-model.number="competitionForm.awdpDefenseScorePerRound" type="number" min="0" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpFixTimeout') }}</Label><Input v-model.number="competitionForm.awdpFixTimeoutSeconds" type="number" min="1" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpMaxAttackAttempts') }}</Label><Input v-model.number="competitionForm.awdpMaxAttackAttempts" type="number" min="1" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpMaxDefenseAttempts') }}</Label><Input v-model.number="competitionForm.awdpMaxDefenseAttempts" type="number" min="1" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpFixEntry') }}</Label><Input v-model="competitionForm.awdpFixEntry" placeholder="fix.sh" /></div>
      </CardContent>
    </Card>

    <Card v-if="competitionForm.gameModeType === 'Awdp'" class="mt-4 p-0">
      <CardContent class="grid gap-4 lg:grid-cols-4 p-4">
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpServicePenalty') }}</Label><Input v-model.number="competitionForm.awdpServicePenaltyPerRound" type="number" min="0" :disabled="!competitionForm.awdpServicePenaltyEnabled" /></div>
        <div class="grid gap-2"><Label>{{ t('admin.competitionDetail.awdpViolationPenalty') }}</Label><Input v-model.number="competitionForm.awdpViolationPenalty" type="number" min="0" :disabled="!competitionForm.awdpViolationPenaltyEnabled" /></div>
        <Panel>
          <label class="flex cursor-pointer items-center gap-3 px-3 py-2 text-sm"><input v-model="competitionForm.awdpAllowAttackAfterBreakSuccess" type="checkbox" class="size-4"><span>{{ t('admin.competitionDetail.awdpAllowAttackRepeat') }}</span></label>
        </Panel>
        <Panel>
          <label class="flex cursor-pointer items-center gap-3 px-3 py-2 text-sm"><input v-model="competitionForm.awdpAllowDefenseAfterFixSuccess" type="checkbox" class="size-4"><span>{{ t('admin.competitionDetail.awdpAllowDefenseRepeat') }}</span></label>
        </Panel>
        <Panel>
          <label class="flex cursor-pointer items-center gap-3 px-3 py-2 text-sm"><input v-model="competitionForm.awdpServicePenaltyEnabled" type="checkbox" class="size-4"><span>{{ t('admin.competitionDetail.awdpEnableServicePenalty') }}</span></label>
        </Panel>
        <Panel>
          <label class="flex cursor-pointer items-center gap-3 px-3 py-2 text-sm"><input v-model="competitionForm.awdpViolationPenaltyEnabled" type="checkbox" class="size-4"><span>{{ t('admin.competitionDetail.awdpEnableViolationPenalty') }}</span></label>
        </Panel>
      </CardContent>
    </Card>

  </Card>
</template>
