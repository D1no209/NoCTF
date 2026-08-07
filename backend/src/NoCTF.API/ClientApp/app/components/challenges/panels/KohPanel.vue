<script setup lang="ts">
import { toast } from 'vue-sonner'
import { Copy } from '@lucide/vue'
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
} from '~/api'

defineProps<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
}>()

async function copyControlFlag(flag: string) {
  try {
    await navigator.clipboard.writeText(flag)
    toast.success('Control Flag 已复制')
  }
  catch {
    toast.error('复制失败,请手动选择复制')
  }
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert>
      <AlertDescription>
        KoH 模式:占领山头并保持控制即可得分。本题目无需提交 flag,也没有需要启动的环境。
      </AlertDescription>
    </Alert>

    <Card>
      <CardHeader>
        <CardTitle class="text-base">山头(Hill)入口</CardTitle>
        <CardDescription>通过以下入口接入山头服务,提交你的 Control Flag 以宣示控制</CardDescription>
      </CardHeader>
      <CardContent>
        <div v-if="challenge.urls?.length" class="flex flex-col gap-1">
          <a
            v-for="url in challenge.urls"
            :key="url"
            :href="url"
            target="_blank"
            rel="noopener"
            class="break-all font-mono text-sm text-primary underline"
          >
            {{ url }}
          </a>
        </div>
        <p v-else class="text-sm text-muted-foreground">山头入口尚未开放,请留意竞赛动态。</p>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardTitle class="text-base">本队 Control Flag</CardTitle>
        <CardDescription>仅本队可见,用于在山头服务中宣示控制权,请勿泄露</CardDescription>
      </CardHeader>
      <CardContent>
        <div v-if="challenge.controlFlag" class="flex flex-wrap items-center gap-2">
          <code class="rounded bg-muted px-2 py-1 font-mono text-sm">{{ challenge.controlFlag }}</code>
          <Button variant="outline" size="sm" @click="copyControlFlag(challenge.controlFlag!)">
            <Copy data-icon="inline-start" />
            复制
          </Button>
        </div>
        <p v-else class="text-sm text-muted-foreground">
          登录并通过报名审核后,这里会显示你队伍的 Control Flag。
        </p>
      </CardContent>
    </Card>
  </div>
</template>
