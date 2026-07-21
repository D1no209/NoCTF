<script setup lang="ts">
import { ArrowLeft, Loader2, Plus, RefreshCw, Save, Trash2 } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import DecayCurvePreview from '@/ui-v1/components/admin/DecayCurvePreview.vue'
import AdminCompetitionChallengeBinder from '@/ui-v1/components/admin/competition-detail/AdminCompetitionChallengeBinder.vue'
import AdminCompetitionCheatIncidentsPanel from '@/ui-v1/components/admin/competition-detail/AdminCompetitionCheatIncidentsPanel.vue'
import AdminCompetitionLogsPanel from '@/ui-v1/components/admin/competition-detail/AdminCompetitionLogsPanel.vue'
import AdminCompetitionSettingsPanel from '@/ui-v1/components/admin/competition-detail/AdminCompetitionSettingsPanel.vue'
import AdminCompetitionTeamsPanel from '@/ui-v1/components/admin/competition-detail/AdminCompetitionTeamsPanel.vue'
import { useToastMutation } from '@/ui-v1/components/feedback/useToastMutation'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import { Card, CardContent } from '@/ui-v1/components/ui/card'
import { Input } from '@/ui-v1/components/ui/input'
import { Label } from '@/ui-v1/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/ui-v1/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/ui-v1/components/ui/table'
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/ui-v1/components/ui/tabs'
import { Textarea } from '@/ui-v1/components/ui/textarea'
import { useAdminCompetitionDetailPage } from '@/features/admin/useAdminCompetitionDetailPage'

const { t } = useI18n()

const competitionDetailSections = [
  { key: 'settings', labelKey: 'admin.competitionDetail.navSettings' },
  { key: 'challenges', labelKey: 'admin.competitionDetail.navChallenges' },
  { key: 'teams', labelKey: 'admin.competitionDetail.navTeams' },
  { key: 'cheats', labelKey: 'admin.competitionDetail.navCheats' },
  { key: 'logs', labelKey: 'admin.competitionDetail.navLogs' },
] as const

const {
  competitionId,
  selectedChallengeId,
  competitionForm,
  bindForm,
  selectedEdit,
  competition,
  loadingCompetition,
  templates,
  competitionChallenges,
  loadingChallenges,
  competitionTeams,
  loadingTeams,
  competitionLogs,
  loadingLogs,
  cheatIncidents,
  loadingCheatIncidents,
  selectedChallenge,
  saveCompetitionMutation: saveCompetition,
  bindMutation: deployChallenge,
  updateChallengeMutation: updateChallenge,
  deleteChallengeMutation: deleteChallenge,
  approveTeamMutation: approveTeam,
  rejectTeamMutation: rejectTeam,
  lockTeamMutation: lockTeam,
  banTeamMutation: banTeam,
  unbanTeamMutation: unbanTeam,
  restartContainerMutation: restartContainer,
  rebuildScoreboardMutation: rebuildScoreboard,
  activeSection,
  switchSection,
  canOpenAwdpScreen,
  isStaticContainer,
  selectChallenge,
  addBindHint,
  addEditHint,
  goAdminCompetitions,
} = useAdminCompetitionDetailPage()

const saveCompetitionMutation = useToastMutation(saveCompetition, {
  success: 'admin.competitionDetail.saveCompetitionSuccess',
  error: 'admin.competitionDetail.saveCompetitionError',
})

const bindMutation = useToastMutation(deployChallenge, {
  success: 'admin.competitionDetail.deploySuccess',
  error: 'admin.competitionDetail.deployError',
})

const updateChallengeMutation = useToastMutation(updateChallenge, {
  success: 'admin.competitionDetail.updateChallengeSuccess',
  error: 'admin.competitionDetail.updateChallengeError',
})

const deleteChallengeMutation = useToastMutation<string>(deleteChallenge, {
  success: 'admin.competitionDetail.removeChallengeSuccess',
  error: 'admin.competitionDetail.removeChallengeError',
})

const approveTeamMutation = useToastMutation<string>(approveTeam, {
  success: 'admin.competitionDetail.teamApproved',
  error: 'admin.competitionDetail.teamActionError',
})

const rejectTeamMutation = useToastMutation<string>(rejectTeam, {
  success: 'admin.competitionDetail.teamRejected',
  error: 'admin.competitionDetail.teamActionError',
})

const lockTeamMutation = useToastMutation<{ teamId: string, isLocked: boolean }>(lockTeam, {
  success: 'admin.competitionDetail.teamLockUpdated',
  error: 'admin.competitionDetail.teamActionError',
})

const banTeamMutation = useToastMutation<string>(banTeam, {
  success: 'admin.competitionDetail.teamBanned',
  error: 'admin.competitionDetail.teamActionError',
})

const unbanTeamMutation = useToastMutation<string>(unbanTeam, {
  success: 'admin.competitionDetail.teamUnbanned',
  error: 'admin.competitionDetail.teamActionError',
})

const restartContainerMutation = useToastMutation<string>(restartContainer, {
  success: 'admin.competitionDetail.containerRestarted',
  error: 'admin.competitionDetail.containerRestartError',
})

const rebuildScoreboardMutation = useToastMutation(rebuildScoreboard, {
  success: (data) => {
    const rows = (data as { rows?: number } | undefined)?.rows
    return `Scoreboard rebuilt${rows === undefined ? '' : ` (${rows} teams)`}`
  },
  error: 'Unable to rebuild scoreboard',
})
</script>

<template>
  <div class="space-y-6">
    <div class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div class="space-y-2">
        <Button variant="ghost" size="sm" class="-ml-2" @click="goAdminCompetitions">
          <ArrowLeft class="mr-2 size-4" />
          {{ t('admin.competitions.title') }}
        </Button>
        <div>
          <h2 class="text-2xl font-bold tracking-tight">
            {{ competition?.title ?? t('admin.competitionDetail.fallbackTitle') }}
          </h2>
          <p class="text-sm text-muted-foreground">
            {{ t('admin.competitionDetail.subtitle') }}
          </p>
        </div>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          :disabled="rebuildScoreboardMutation.isPending.value"
          @click="rebuildScoreboardMutation.mutate()"
        >
          <Loader2 v-if="rebuildScoreboardMutation.isPending.value" class="size-4 animate-spin" />
          <RefreshCw v-else class="size-4" />
          Rebuild scoreboard
        </Button>
        <Button variant="outline" size="sm" as-child>
          <RouterLink :to="{ name: 'admin-competition-operations', params: { id: competitionId } }">
            Operations
          </RouterLink>
        </Button>
        <Button v-if="canOpenAwdpScreen" variant="outline" size="sm" as-child>
          <RouterLink :to="{ name: 'awdp-screen', params: { gameId: competitionId } }">
            {{ t('awdp.screenEntry') }}
          </RouterLink>
        </Button>
        <Badge v-if="competition?.status" variant="outline" class="capitalize">
          {{ competition.status }}
        </Badge>
      </div>
    </div>

    <div v-if="loadingCompetition" class="flex flex-col items-center justify-center py-12 text-center text-sm text-muted-foreground">
      <Loader2 class="mr-2 inline size-4 animate-spin" />
      {{ t('admin.competitionDetail.loadingCompetition') }}
    </div>

    <Tabs
      v-else
      :model-value="activeSection"
      @update:model-value="(value: string | undefined) => value && switchSection(value)"
    >
      <TabsList>
        <TabsTrigger
          v-for="section in competitionDetailSections"
          :key="section.key"
          :value="section.key"
        >
          {{ t(section.labelKey) }}
        </TabsTrigger>
      </TabsList>

      <div
        class="grid gap-6"
        :class="activeSection === 'challenges' ? 'xl:grid-cols-[minmax(0,1fr)_420px]' : ''"
      >
        <section class="space-y-6">
          <TabsContent value="settings">
            <AdminCompetitionSettingsPanel
              v-if="activeSection === 'settings'"
              :competition-form="competitionForm"
              :saving="saveCompetitionMutation.isPending.value"
              @save="saveCompetitionMutation.mutate()"
            />
          </TabsContent>

          <TabsContent value="challenges">
            <AdminCompetitionChallengeBinder
              v-if="activeSection === 'challenges'"
              :bind-form="bindForm"
              :templates="templates ?? []"
              :competition-form="competitionForm"
              :deploying="bindMutation.isPending.value"
              @add-hint="addBindHint"
              @deploy="bindMutation.mutate()"
            />
          </TabsContent>

          <TabsContent value="teams">
            <AdminCompetitionTeamsPanel
              v-if="activeSection === 'teams'"
              :competition-teams="competitionTeams"
              :loading-teams="loadingTeams"
              :max-team-members="competitionForm.maxTeamMembers"
              @approve="approveTeamMutation.mutate($event)"
              @reject="rejectTeamMutation.mutate($event)"
              @toggle-lock="lockTeamMutation.mutate($event)"
              @toggle-ban="$event.isBanned ? unbanTeamMutation.mutate($event.teamId) : banTeamMutation.mutate($event.teamId)"
            />
          </TabsContent>

          <TabsContent value="cheats">
            <AdminCompetitionCheatIncidentsPanel
              v-if="activeSection === 'cheats'"
              :cheat-incidents="cheatIncidents"
              :loading-cheat-incidents="loadingCheatIncidents"
              @ban="banTeamMutation.mutate($event)"
            />
          </TabsContent>

          <TabsContent value="logs">
            <AdminCompetitionLogsPanel
              v-if="activeSection === 'logs'"
              :competition-logs="competitionLogs"
              :loading-logs="loadingLogs"
            />
          </TabsContent>
        </section>

        <aside v-if="activeSection === 'challenges'" class="space-y-6">
          <Card class="p-0 overflow-hidden">
            <div class="border-b p-4">
              <h3 class="font-semibold">
                {{ t('admin.competitionDetail.competitionChallenges') }}
              </h3>
              <p class="text-sm text-muted-foreground">
                {{ t('admin.competitionDetail.competitionChallengesDescription') }}
              </p>
            </div>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{{ t('admin.challenges.titleColumn') }}</TableHead>
                  <TableHead>{{ t('common.points') }}</TableHead>
                  <TableHead class="w-10" />
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-if="loadingChallenges">
                  <TableCell colspan="3" class="h-20 text-center text-muted-foreground">
                    <Loader2 class="mr-2 inline size-4 animate-spin" />
                    {{ t('common.loading') }}
                  </TableCell>
                </TableRow>
                <TableRow v-else-if="!competitionChallenges?.length">
                  <TableCell colspan="3" class="h-20 text-center text-muted-foreground">
                    {{ t('admin.competitionDetail.noDeployedChallenges') }}
                  </TableCell>
                </TableRow>
                <TableRow
                  v-for="challenge in competitionChallenges"
                  v-else
                  :key="challenge.id"
                  class="cursor-pointer hover:bg-muted/50"
                  :class="selectedChallengeId === challenge.id ? 'bg-muted/70' : ''"
                  @click="selectChallenge(challenge)"
                >
                  <TableCell>
                    <div class="font-medium">
                      {{ challenge.title }}
                    </div>
                    <div class="text-xs text-muted-foreground">
                      {{ challenge.typeId }}
                    </div>
                  </TableCell>
                  <TableCell class="text-xs">
                    {{ challenge.pointsConfig.minimumPoints }} → {{ challenge.pointsConfig.initialPoints }}
                  </TableCell>
                  <TableCell>
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-8 text-destructive"
                      @click.stop="deleteChallengeMutation.mutate(challenge.id)"
                    >
                      <Trash2 class="size-4" />
                    </Button>
                    <Button
                      v-if="isStaticContainer(challenge)"
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      @click.stop="restartContainerMutation.mutate(challenge.id)"
                    >
                      <Loader2 v-if="restartContainerMutation.isPending.value" class="size-4 animate-spin" />
                      <Save v-else class="size-4" />
                    </Button>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </Card>

          <Card v-if="selectedChallenge" class="p-4">
            <div class="mb-4">
              <h3 class="font-semibold">
                {{ selectedChallenge.title }}
              </h3>
              <p class="text-sm text-muted-foreground">
                {{ t('admin.competitionDetail.editChallengeDescription') }}
              </p>
            </div>

            <div class="space-y-4">
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.markdownDescriptionShort') }}</Label>
                <Textarea v-model="selectedEdit.description" class="font-mono text-xs" rows="7" />
              </div>
              <div class="grid grid-cols-2 gap-3">
                <div class="grid gap-2">
                  <Label>{{ t('admin.competitionDetail.initial') }}</Label>
                  <Input v-model.number="selectedEdit.initialPoints" type="number" />
                </div>
                <div class="grid gap-2">
                  <Label>{{ t('admin.competitionDetail.minimum') }}</Label>
                  <Input v-model.number="selectedEdit.minimumPoints" type="number" />
                </div>
                <div class="grid gap-2">
                  <Label>{{ t('admin.competitionDetail.decayFactor') }}</Label>
                  <Input v-model.number="selectedEdit.decayFactor" type="number" />
                </div>
                <div class="grid gap-2">
                  <Label>{{ t('admin.competitionDetail.difficulty') }}</Label>
                  <Input v-model.number="selectedEdit.difficultyCoefficient" type="number" min="0.1" step="0.1" />
                </div>
              </div>
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.decayFunction') }}</Label>
                <Select v-model="selectedEdit.decayFunction">
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="sigmoid">
                      {{ t('admin.competitionDetail.decaySigmoid') }}
                    </SelectItem>
                    <SelectItem value="quadratic">
                      {{ t('admin.competitionDetail.decayQuadratic') }}
                    </SelectItem>
                    <SelectItem value="logarithmic">
                      {{ t('admin.competitionDetail.decayLogarithmic') }}
                    </SelectItem>
                    <SelectItem value="linear">
                      {{ t('admin.competitionDetail.decayLinear') }}
                    </SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <label v-if="competitionForm.gameModeType === 'Ctf'" class="flex items-center gap-3 rounded-lg border bg-muted/30 px-3 py-2 text-sm">
                <input v-model="selectedEdit.enableBloodBonus" type="checkbox" class="size-4">
                <span>{{ t('admin.competitionDetail.enableBloodBonus') }}</span>
              </label>
              <DecayCurvePreview :config="selectedEdit" />
              <div class="grid gap-2">
                <Label>{{ t('admin.competitionDetail.flagPrefix') }}</Label>
                <Input v-model="selectedEdit.flagPrefix" placeholder="flag" />
              </div>
              <Card v-if="competitionForm.gameModeType === 'Awdp'" class="p-0">
                <CardContent class="grid gap-3 p-3 sm:grid-cols-2">
                  <div class="grid gap-2">
                    <Label>{{ t('admin.competitionDetail.awdpAttackScore') }}</Label>
                    <Input v-model.number="selectedEdit.awdpAttackScorePerRound" type="number" min="0" />
                  </div>
                  <div class="grid gap-2">
                    <Label>{{ t('admin.competitionDetail.awdpDefenseScore') }}</Label>
                    <Input v-model.number="selectedEdit.awdpDefenseScorePerRound" type="number" min="0" />
                  </div>
                  <div class="grid gap-2">
                    <Label>{{ t('admin.competitionDetail.awdpMaxAttackAttempts') }}</Label>
                    <Input v-model.number="selectedEdit.awdpMaxAttackAttempts" type="number" min="1" />
                  </div>
                  <div class="grid gap-2">
                    <Label>{{ t('admin.competitionDetail.awdpMaxDefenseAttempts') }}</Label>
                    <Input v-model.number="selectedEdit.awdpMaxDefenseAttempts" type="number" min="1" />
                  </div>
                  <div class="grid gap-2">
                    <Label>{{ t('admin.competitionDetail.awdpFixEntry') }}</Label>
                    <Input v-model="selectedEdit.awdpFixEntry" placeholder="fix.sh" />
                  </div>
                  <div class="grid gap-2">
                    <Label>{{ t('admin.competitionDetail.awdpFixTimeout') }}</Label>
                    <Input v-model.number="selectedEdit.awdpFixTimeoutSeconds" type="number" min="1" />
                  </div>
                </CardContent>
              </Card>
              <div class="space-y-2">
                <div class="flex items-center justify-between">
                  <Label>{{ t('admin.competitionDetail.hints') }}</Label>
                  <Button variant="outline" size="sm" @click="addEditHint">
                    <Plus class="mr-2 size-4" />
                    {{ t('common.add') }}
                  </Button>
                </div>
                <Input v-for="(_, index) in selectedEdit.hints" :key="index" v-model="selectedEdit.hints[index]" :placeholder="t('admin.competitionDetail.hintPlaceholder', { index: index + 1 })" />
              </div>
              <Button class="w-full" :disabled="updateChallengeMutation.isPending.value" @click="updateChallengeMutation.mutate()">
                <Loader2 v-if="updateChallengeMutation.isPending.value" class="mr-2 size-4 animate-spin" />
                {{ t('admin.competitionDetail.saveDeployedChallenge') }}
              </Button>
            </div>
          </Card>
        </aside>
      </div>
    </Tabs>
  </div>
</template>
