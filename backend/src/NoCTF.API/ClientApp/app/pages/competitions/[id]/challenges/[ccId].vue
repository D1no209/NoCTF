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

const route = useRoute()
const competitionId = route.params.id as string
const competitionChallengeId = route.params.ccId as string
const ctx = inject(competitionContextKey)!
const { isLoggedIn } = useAuth()

const challenge = ref<NoCtfapiEndpointsChallengesChallengeResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  const { data, error: err } = await getChallengeEndpoint({
    path: { competitionId, competitionChallengeId },
  })
  loading.value = false
  if (err || !data) {
    error.value = parseApiError(err, translate("加载题目失败")).message
    return
  }
  challenge.value = data
})

// 附件(需要登录)
const attachments = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeAttachmentResponse[]>([])
const attachmentDeliveryPolicy = ref<NoCtfapiEndpointsAdministrationChallengeBankAttachmentDeliveryPolicyProtocol>('All')
const attachmentsLoaded = ref(false)

async function loadAttachments() {
  if (!isLoggedIn.value) return
  const { data, error: err } = await listChallengeAttachmentsEndpoint({
    path: { competitionId, competitionChallengeId },
  })
  attachmentsLoaded.value = true
  if (err || !data) return
  attachmentDeliveryPolicy.value = data.deliveryPolicy ?? 'All'
  attachments.value = (data.items ?? []).filter((a) => !a.deletedAt)
}

onMounted(loadAttachments)
watch(isLoggedIn, loadAttachments)

const downloading = ref(false)

async function downloadAttachment(attachmentId: string, fileName: string) {
  downloading.value = true
  try {
    await downloadSdkFile(
      downloadChallengeAttachmentEndpoint({
        path: { competitionId, competitionChallengeId, attachmentId },
        parseAs: 'blob',
      }),
      fileName,
    )
  }
  catch (e) {
    toast.error(parseApiError(e, translate("附件下载失败")).message)
  }
  finally {
    downloading.value = false
  }
}

async function downloadRandom() {
  downloading.value = true
  try {
    await downloadSdkFile(
      downloadRandomChallengeAttachmentEndpoint({
        path: { competitionId, competitionChallengeId },
        parseAs: 'blob',
      }),
      'attachment',
    )
  }
  catch (e) {
    toast.error(parseApiError(e, translate("附件下载失败")).message)
  }
  finally {
    downloading.value = false
  }
}

const mode = computed(() => ctx.competition.value?.mode)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div v-else-if="loading" class="flex flex-col gap-4">
      <Skeleton class="h-8 w-1/2" />
      <Skeleton class="h-40 w-full" />
    </div>

    <template v-else-if="challenge">
      <div class="flex flex-wrap items-center gap-3">
        <h2 class="text-display text-2xl">{{ challenge.title }}</h2>
        <Badge variant="outline" :class="directionBadgeClass(challenge.direction)">
          {{ challenge.direction }}
        </Badge>
        <Badge v-if="challenge.baseScore === null || challenge.baseScore === undefined" variant="secondary"> {{ $t('分数隐藏') }} </Badge>
        <span v-else class="font-mono text-lg font-semibold text-primary tabular-nums">{{ $t('{score} 分', { score: challenge.baseScore }) }}</span>
      </div>

      <Card>
        <CardHeader>
          <CardTitle class="text-base">{{ $t('题面') }}</CardTitle>
        </CardHeader>
        <CardContent>
          <p v-if="challenge.description" class="whitespace-pre-line text-sm leading-6">
            {{ challenge.description }}
          </p>
          <p v-else class="text-sm text-muted-foreground">{{ $t('本题没有额外描述。') }}</p>
        </CardContent>
      </Card>

      <Card v-if="attachments.length || isLoggedIn">
        <CardHeader>
          <div class="flex items-center justify-between gap-2">
            <CardTitle class="text-base">{{ $t('附件') }}</CardTitle>
            <Button
              v-if="attachmentDeliveryPolicy === 'RandomOnePerTeam'"
              variant="outline"
              size="sm"
              :disabled="downloading"
              @click="downloadRandom"
            >
              <Dice5 data-icon="inline-start" /> {{ $t('下载附件') }}
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <p v-if="!attachmentsLoaded" class="text-sm text-muted-foreground">{{ $t('加载中…') }}</p>
          <p v-else-if="attachmentDeliveryPolicy === 'RandomOnePerTeam'" class="text-sm text-muted-foreground">
            {{ $t('首次下载会为本队随机分配一个附件，之后始终获得同一文件。') }}
          </p>
          <p v-else-if="!attachments.length" class="text-sm text-muted-foreground">{{ $t('本题没有附件。') }}</p>
          <ul v-else class="flex flex-col gap-2">
            <li
              v-for="attachment in attachments"
              :key="attachment.id"
              class="flex items-center justify-between gap-2 rounded-md border px-3 py-2"
            >
              <span class="flex items-center gap-2 text-sm">
                <FileDown class="size-4 text-muted-foreground" />
                {{ attachment.fileName }}
                <span class="text-muted-foreground">{{ formatBytes(attachment.byteLength) }}</span>
              </span>
              <Button
                variant="ghost"
                size="sm"
                :disabled="downloading"
                @click="downloadAttachment(attachment.id!, attachment.fileName ?? 'attachment')"
              >
                <Download data-icon="inline-start" /> {{ $t('下载') }} </Button>
            </li>
          </ul>
        </CardContent>
      </Card>

      <template v-if="ctx.competition.value">
        <CtfPanel
          v-if="mode === 'Ctf'"
          :competition="ctx.competition.value"
          :challenge="challenge"
        />
        <AwdPanel
          v-else-if="mode === 'Awd'"
          :competition="ctx.competition.value"
          :challenge="challenge"
        />
        <AwdpPanel
          v-else-if="mode === 'Awdp'"
          :competition="ctx.competition.value"
          :challenge="challenge"
        />
        <KohPanel
          v-else-if="mode === 'Koh'"
          :competition="ctx.competition.value"
          :challenge="challenge"
        />
      </template>
    </template>
  </div>
</template>
