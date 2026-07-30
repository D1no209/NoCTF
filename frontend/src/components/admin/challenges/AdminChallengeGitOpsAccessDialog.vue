<script setup lang="ts">
import type { ChallengeTemplate } from '@/api/noctf'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Bot, Loader2, RefreshCw, ShieldCheck } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { challengeBankAdminApi, platformAdminApi, readChallengeTemplateConflict } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import {
  buildChallengeGitOpsAccessUpdate,
  existingChallengeManagerIds,
  gitOpsBotCandidates,
} from './challengeGitOpsAccess'

const props = defineProps<{
  open: boolean
  template: ChallengeTemplate | null
}>()

const emit = defineEmits<{
  'update:open': [open: boolean]
}>()

const { t } = useI18n()
const queryClient = useQueryClient()
const selectedBotId = ref('')

const templateId = computed(() => props.template?.id ?? '')
const templateQueryKey = computed(() => queryKeys.adminChallenge(templateId.value || 'unselected'))
const queryEnabled = computed(() => props.open && Boolean(templateId.value))

const templateQuery = useQuery({
  queryKey: templateQueryKey,
  queryFn: () => challengeBankAdminApi.template(templateId.value),
  enabled: queryEnabled,
})

const usersQuery = useQuery({
  queryKey: queryKeys.adminUsers,
  queryFn: platformAdminApi.users,
  enabled: computed(() => props.open),
})

const currentTemplate = computed(() =>
  templateQuery.data.value?.id === templateId.value ? templateQuery.data.value : props.template,
)
const currentManagerIds = computed(() =>
  currentTemplate.value ? existingChallengeManagerIds(currentTemplate.value) : [],
)
const candidateBots = computed(() =>
  currentTemplate.value
    ? gitOpsBotCandidates(usersQuery.data.value ?? [], currentTemplate.value)
    : [],
)
const selectedBot = computed(() =>
  candidateBots.value.find(bot => bot.id === selectedBotId.value),
)
const pendingUpdate = computed(() =>
  currentTemplate.value && selectedBot.value?.id
    ? buildChallengeGitOpsAccessUpdate(currentTemplate.value, selectedBot.value.id)
    : null,
)
const isLoading = computed(() => templateQuery.isLoading.value || usersQuery.isLoading.value)
const isError = computed(() => templateQuery.isError.value || usersQuery.isError.value)

const grantMutation = useMutation({
  mutationFn: async () => {
    const update = pendingUpdate.value
    if (!update)
      throw new Error('Challenge GitOps access update is incomplete.')

    return await challengeBankAdminApi.updatePermissions(
      update.challengeId,
      update.managerIds,
      update.expectedRevision,
    )
  },
  onSuccess: (updatedTemplate) => {
    const id = updatedTemplate.id ?? templateId.value
    queryClient.setQueryData(queryKeys.adminChallenge(id), updatedTemplate)
    void queryClient.invalidateQueries({ queryKey: queryKeys.adminChallenges })
    selectedBotId.value = ''
    emit('update:open', false)
    toast.success(t('admin.challenges.gitOpsAccessGranted'))
  },
  onError: async (error) => {
    const conflict = readChallengeTemplateConflict(error)
    selectedBotId.value = ''
    await Promise.all([templateQuery.refetch(), usersQuery.refetch()])
    void queryClient.invalidateQueries({ queryKey: queryKeys.adminChallenges })

    if (conflict) {
      toast.error(t(challengeConflictMessageKey(conflict.code)))
      return
    }

    toast.error(t('admin.challenges.gitOpsAccessError'))
  },
})

watch(
  () => [props.open, props.template?.id] as const,
  () => {
    selectedBotId.value = ''
    grantMutation.reset()
  },
)

function challengeConflictMessageKey(
  code: NonNullable<ReturnType<typeof readChallengeTemplateConflict>>['code'],
) {
  switch (code) {
    case 'RevisionConflict':
      return 'admin.challenges.gitOpsAccessRevisionConflict'
    case 'UserNotFound':
      return 'admin.challenges.gitOpsAccessUserNotFound'
    case 'RoleNotEligible':
      return 'admin.challenges.gitOpsAccessRoleNotEligible'
    case 'OwnerIncludedInManagerSet':
      return 'admin.challenges.gitOpsAccessOwnerConflict'
    default:
      return 'admin.challenges.gitOpsAccessError'
  }
}

function displayBotName() {
  return selectedBot.value?.userName || selectedBot.value?.id || ''
}

function refreshDialogData() {
  void templateQuery.refetch()
  void usersQuery.refetch()
}

function onOpenChange(open: boolean) {
  if (!open && grantMutation.isPending.value)
    return
  emit('update:open', open)
}
</script>

<template>
  <Dialog :open="open" @update:open="onOpenChange">
    <DialogContent class="sm:max-w-[560px]">
      <DialogHeader>
        <DialogTitle>{{ t('admin.challenges.gitOpsAccessTitle') }}</DialogTitle>
        <DialogDescription>
          {{ t('admin.challenges.gitOpsAccessDescription') }}
        </DialogDescription>
      </DialogHeader>

      <div v-if="isLoading" class="space-y-3 py-3">
        <Skeleton class="h-20 w-full" />
        <Skeleton class="h-10 w-full" />
      </div>

      <Alert v-else-if="isError" variant="destructive">
        <AlertTitle>{{ t('admin.challenges.gitOpsAccessLoadError') }}</AlertTitle>
        <AlertDescription class="mt-3">
          <Button variant="outline" size="sm" @click="refreshDialogData">
            <RefreshCw class="size-4" />
            {{ t('common.refresh') }}
          </Button>
        </AlertDescription>
      </Alert>

      <div v-else-if="currentTemplate" class="space-y-5 py-2">
        <div class="border bg-muted/30 p-4">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <p class="font-medium">
              {{ currentTemplate.title || currentTemplate.id }}
            </p>
            <Badge variant="outline" class="font-mono">
              v{{ currentTemplate.revision ?? 0 }}
            </Badge>
          </div>
          <code class="mt-2 block break-all font-mono text-xs text-muted-foreground">
            {{ currentTemplate.id }}
          </code>
        </div>

        <div class="space-y-2">
          <div class="flex items-center justify-between gap-3">
            <Label>{{ t('admin.challenges.existingManagers') }}</Label>
            <span class="text-xs tabular-nums text-muted-foreground">
              {{ currentManagerIds.length }}
            </span>
          </div>
          <div
            v-if="currentManagerIds.length"
            class="max-h-28 space-y-1 overflow-y-auto border p-3"
          >
            <code
              v-for="managerId in currentManagerIds"
              :key="managerId"
              class="block break-all font-mono text-xs text-muted-foreground"
            >
              {{ managerId }}
            </code>
          </div>
          <p v-else class="text-xs text-muted-foreground">
            {{ t('admin.challenges.noExistingManagers') }}
          </p>
          <p class="text-xs text-muted-foreground">
            {{ t('admin.challenges.existingManagersPreserved') }}
          </p>
        </div>

        <div class="space-y-2">
          <Label for="gitops-organizer-bot">
            {{ t('admin.challenges.organizerBot') }}
          </Label>
          <Select v-model="selectedBotId" :disabled="candidateBots.length === 0">
            <SelectTrigger id="gitops-organizer-bot">
              <SelectValue :placeholder="t('admin.challenges.selectOrganizerBot')" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="bot in candidateBots" :key="bot.id" :value="bot.id ?? ''">
                {{ bot.userName || bot.id }}
              </SelectItem>
            </SelectContent>
          </Select>
          <p v-if="candidateBots.length === 0" class="text-xs text-muted-foreground">
            {{ t('admin.challenges.noOrganizerBots') }}
          </p>
        </div>

        <Alert v-if="selectedBot" variant="default">
          <div class="flex gap-3">
            <Bot class="mt-0.5 size-4 shrink-0" />
            <div>
              <AlertTitle>{{ displayBotName() }}</AlertTitle>
              <AlertDescription>
                {{ t('admin.challenges.gitOpsAccessGrantSummary') }}
              </AlertDescription>
            </div>
          </div>
        </Alert>
      </div>

      <DialogFooter>
        <Button
          variant="outline"
          :disabled="grantMutation.isPending.value"
          @click="onOpenChange(false)"
        >
          {{ t('common.cancel') }}
        </Button>
        <Button
          :disabled="grantMutation.isPending.value || !pendingUpdate"
          @click="grantMutation.mutate()"
        >
          <Loader2 v-if="grantMutation.isPending.value" class="size-4 animate-spin" />
          <ShieldCheck v-else class="size-4" />
          {{ t('admin.challenges.grantGitOpsAccess') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
