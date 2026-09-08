<script setup lang="ts">
import { toast } from 'vue-sonner'
import { Copy } from '@lucide/vue'
import { publicGatewayFailure } from '~/utils/public-gateway'
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
    toast.success(translate("Control Flag 已复制"))
  }
  catch {
    toast.error(translate("复制失败,请手动选择复制"))
  }
}
</script>

<template>
  <div class="grid gap-6 md:grid-cols-2 md:gap-0 md:divide-x">
    <section class="flex flex-col gap-4 md:pr-6" aria-labelledby="koh-hill-title">
        <h3 id="koh-hill-title" class="text-sm font-semibold">{{ $t('山头(Hill)入口') }}</h3>
        <Alert v-if="challenge.publicAccessFailure"><AlertDescription>{{ publicGatewayFailure(challenge.publicAccessFailure) }}</AlertDescription></Alert>
        <div v-else-if="challenge.urls?.length" class="flex flex-col gap-1">
          <RuntimeAccessUrl
            v-for="url in challenge.urls"
            :key="url"
            :url="url"
          />
        </div>
        <p v-else class="text-sm text-muted-foreground">{{ $t('山头入口尚未开放,请留意竞赛动态。') }}</p>
    </section>

    <section class="flex flex-col gap-4 border-t pt-6 md:border-t-0 md:pl-6 md:pt-0" aria-labelledby="koh-control-title">
        <header>
          <h3 id="koh-control-title" class="text-sm font-semibold">{{ $t('本队 Control Flag') }}</h3>
          <p class="mt-1 text-sm text-muted-foreground">{{ $t('仅本队可见,用于在山头服务中宣示控制权,请勿泄露') }}</p>
        </header>
        <div v-if="challenge.controlFlag" class="flex flex-wrap items-center gap-2">
          <code class="rounded bg-muted px-2 py-1 font-mono text-sm">{{ challenge.controlFlag }}</code>
          <Button variant="outline" size="sm" @click="copyControlFlag(challenge.controlFlag!)">
            <Copy data-icon="inline-start" /> {{ $t('复制') }} </Button>
        </div>
        <p v-else class="text-sm text-muted-foreground"> {{ $t('登录并通过报名审核后,这里会显示你队伍的 Control Flag。') }} </p>
    </section>
  </div>
</template>
