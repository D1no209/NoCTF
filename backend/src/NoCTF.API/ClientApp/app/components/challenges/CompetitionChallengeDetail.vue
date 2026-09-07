<script setup lang="ts">
import { toast } from 'vue-sonner'
import { Dice5, Download, FileDown } from '@lucide/vue'
import {
  downloadChallengeAttachmentEndpoint,
  downloadRandomChallengeAttachmentEndpoint,
  getChallengeEndpoint,
  listChallengeAttachmentsEndpoint,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationChallengeBankAttachmentDeliveryPolicyProtocol,
  NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse,
  NoCtfapiEndpointsChallengesChallengeResponse,
} from '~/api'
import { downloadSdkFile } from '~/utils/download'

const props = defineProps<{
  competitionId: string
  competitionChallengeId: string
}>()

const ctx = inject(competitionContextKey)!
const { isLoggedIn, user } = useAuth()
const challenge = ref<NoCtfapiEndpointsChallengesChallengeResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const attachments = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse[]>([])
const attachmentDeliveryPolicy = ref<NoCtfapiEndpointsAdministrationChallengeBankAttachmentDeliveryPolicyProtocol>('All')
const attachmentsLoading = ref(false)
const attachmentError = ref<string | null>(null)
const downloading = ref(false)
const historyRefreshKey = ref(0)
let loadSequence = 0

function refreshSubmissionHistory(): void {
  historyRefreshKey.value += 1
}

async function loadChallenge(): Promise<void> {
  const sequence = ++loadSequence
  loading.value = true
  error.value = null
  challenge.value = null
  attachments.value = []
  attachmentDeliveryPolicy.value = 'All'

  const { data, error: requestError } = await getChallengeEndpoint({
    path: {
      competitionId: props.competitionId,
      competitionChallengeId: props.competitionChallengeId,
    },
  })
  if (sequence !== loadSequence) return
  loading.value = false
  if (requestError || !data) {
    error.value = parseApiError(requestError, translate('加载题目失败')).message
    return
  }
  challenge.value = data
}

async function loadAttachments(): Promise<void> {
  if (!isLoggedIn.value) {
    attachments.value = []
    attachmentDeliveryPolicy.value = 'All'
    attachmentsLoading.value = false
    attachmentError.value = null
    return
  }
  const challengeId = props.competitionChallengeId
  attachmentsLoading.value = true
  attachmentError.value = null
  const { data, error: requestError } = await listChallengeAttachmentsEndpoint({
    path: {
      competitionId: props.competitionId,
      competitionChallengeId: challengeId,
    },
  })
  if (challengeId !== props.competitionChallengeId) return
  attachmentsLoading.value = false
  if (requestError || !data) {
    attachmentError.value = parseApiError(requestError, translate('加载题目附件失败')).message
    return
  }
  attachmentDeliveryPolicy.value = data.deliveryPolicy ?? 'All'
  attachments.value = (data.items ?? []).filter(attachment => !attachment.deletedAt)
}

watch(
  () => [props.competitionId, props.competitionChallengeId] as const,
  () => {
    void loadChallenge()
    void loadAttachments()
  },
  { immediate: true },
)
watch(isLoggedIn, () => void loadAttachments())
watch(() => user.value?.userId, () => void loadChallenge())

async function downloadAttachment(attachmentId: string, fileName: string): Promise<void> {
  downloading.value = true
  try {
    await downloadSdkFile(
      downloadChallengeAttachmentEndpoint({
        path: {
          competitionId: props.competitionId,
          competitionChallengeId: props.competitionChallengeId,
          attachmentId,
        },
        parseAs: 'blob',
      }),
      fileName,
    )
  }
  catch (downloadError) {
    toast.error(parseApiError(downloadError, translate('附件下载失败')).message)
  }
  finally {
    downloading.value = false
  }
}

async function downloadRandom(): Promise<void> {
  downloading.value = true
  try {
    await downloadSdkFile(
      downloadRandomChallengeAttachmentEndpoint({
        path: {
          competitionId: props.competitionId,
          competitionChallengeId: props.competitionChallengeId,
        },
        parseAs: 'blob',
      }),
      'attachment',
    )
  }
  catch (downloadError) {
    toast.error(parseApiError(downloadError, translate('附件下载失败')).message)
  }
  finally {
    downloading.value = false
  }
}

const mode = computed(() => ctx.competition.value?.mode)
</script>

<template>
  <section class="min-w-0" aria-live="polite">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div v-else-if="loading" class="flex flex-col gap-4">
      <Skeleton class="h-8 w-1/2" />
      <Skeleton class="h-40 w-full" />
      <Skeleton class="h-48 w-full" />
    </div>

    <div v-else-if="challenge" class="flex flex-col">
      <header class="flex flex-wrap items-center gap-3 border-b pb-5">
        <h2 class="text-display text-2xl">{{ challenge.title }}</h2>
        <Badge variant="outline" :class="directionBadgeClass(challenge.direction)">
          {{ directionLabel(challenge.direction) }}
        </Badge>
        <Badge v-if="mode === 'Awdp'" variant="secondary">{{ $t('分值按轮结算') }}</Badge>
      </header>

      <section v-if="challenge.description" class="border-b py-5" :aria-label="challenge.title">
        <MarkdownContent :source="challenge.description" class="text-foreground/90" />
      </section>

      <section
        v-if="isLoggedIn && (attachmentsLoading || attachmentError || attachments.length || attachmentDeliveryPolicy === 'RandomOnePerTeam')"
        class="border-b py-5"
        aria-labelledby="challenge-attachments-title"
      >
        <div class="flex items-center justify-between gap-3">
            <h3 id="challenge-attachments-title" class="text-sm font-semibold">{{ $t('附件') }}</h3>
            <Button
              v-if="!attachmentsLoading && !attachmentError && attachmentDeliveryPolicy === 'RandomOnePerTeam'"
              variant="outline"
              size="sm"
              :disabled="downloading"
              @click="downloadRandom"
            >
              <Dice5 data-icon="inline-start" /> {{ $t('下载附件') }}
            </Button>
        </div>
          <Skeleton v-if="attachmentsLoading" class="mt-3 h-12 w-full" />
          <Alert v-else-if="attachmentError" variant="destructive" class="mt-3">
            <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
              <span>{{ attachmentError }}</span>
              <Button type="button" size="sm" variant="outline" @click="loadAttachments">{{ $t('重新加载') }}</Button>
            </AlertDescription>
          </Alert>
          <ul v-else-if="attachmentDeliveryPolicy !== 'RandomOnePerTeam'" class="mt-3 divide-y border-y">
            <li
              v-for="attachment in attachments"
              :key="attachment.id"
              class="flex items-center justify-between gap-3 py-2.5"
            >
              <span class="flex min-w-0 items-center gap-2 text-sm">
                <FileDown class="size-4 shrink-0 text-muted-foreground" />
                <span class="truncate">{{ attachment.fileName }}</span>
                <span class="shrink-0 text-muted-foreground">{{ formatBytes(attachment.byteLength) }}</span>
              </span>
              <Button
                variant="ghost"
                size="sm"
                :disabled="downloading"
                @click="downloadAttachment(attachment.id!, attachment.fileName ?? 'attachment')"
              >
                <Download data-icon="inline-start" /> {{ $t('下载') }}
              </Button>
            </li>
          </ul>
      </section>

      <ChallengeHints
        :key="`${competitionId}:${challenge.id}:${user?.userId ?? 'anonymous'}`"
        :competition-id="competitionId"
        :competition-challenge-id="competitionChallengeId"
        :hints="challenge.hints"
        @unlocked="refreshSubmissionHistory"
      />

      <div v-if="ctx.competition.value" class="pt-5">
        <CtfPanel
          v-if="mode === 'Ctf'"
          :key="challenge.id"
          :competition="ctx.competition.value"
          :challenge="challenge"
          @submitted="refreshSubmissionHistory"
        />
        <AwdPanel
          v-else-if="mode === 'Awd'"
          :key="challenge.id"
          :competition="ctx.competition.value"
          :challenge="challenge"
          @submitted="refreshSubmissionHistory"
        />
        <AwdpPanel
          v-else-if="mode === 'Awdp'"
          :key="challenge.id"
          :competition="ctx.competition.value"
          :challenge="challenge"
          @submitted="refreshSubmissionHistory"
        />
        <KohPanel
          v-else-if="mode === 'Koh'"
          :key="challenge.id"
          :competition="ctx.competition.value"
          :challenge="challenge"
        />
      </div>

      <ChallengeSubmissionHistory
        v-if="isLoggedIn"
        :key="challenge.id"
        class="mt-5"
        :competition-id="competitionId"
        :competition-challenge-id="competitionChallengeId"
        :refresh-key="historyRefreshKey"
      />
    </div>
  </section>
</template>
