<script setup lang="ts">
import { toRefs } from 'vue'
import type { KohPanelViewState } from '~/features/challenges/panels/useKohPanel'

const viewProps = defineProps<{ state: KohPanelViewState }>()
const { Copy, copyControlFlag, RuntimeAccessUrl, challenge } = toRefs(viewProps.state)
</script>

<template>
  <div class="grid gap-6 md:grid-cols-2 md:gap-0 md:divide-x">
    <section class="flex flex-col gap-4 md:pr-6" aria-labelledby="koh-hill-title">
        <h3 id="koh-hill-title" class="text-sm font-semibold">{{ $t('ui.hillEntrance') }}</h3>
        <div v-if="challenge.accesses?.length" class="flex flex-col gap-1">
          <component :is="RuntimeAccessUrl"
            v-for="access in challenge.accesses"
            :key="`${access.directAddress}:${access.webSocketAddress}`"
            :access="access"
          />
        </div>
        <p v-else class="text-sm text-muted-foreground">{{ $t('ui.theEntranceToTheMountainIsNotYetOpenPlease') }}</p>
    </section>

    <section class="flex flex-col gap-4 border-t pt-6 md:border-t-0 md:pl-6 md:pt-0" aria-labelledby="koh-control-title">
        <header>
          <h3 id="koh-control-title" class="text-sm font-semibold">{{ $t('ui.teamControlFlag') }}</h3>
          <p class="mt-1 text-sm text-muted-foreground">{{ $t('ui.itIsOnlyVisibleToThisTeamItIsUsed') }}</p>
        </header>
        <div v-if="challenge.controlFlag" class="flex flex-wrap items-center gap-2">
          <code class="rounded bg-muted px-2 py-1 font-mono text-sm">{{ challenge.controlFlag }}</code>
          <Button variant="outline" size="sm" @click="copyControlFlag(challenge.controlFlag!)">
            <Copy data-icon="inline-start" /> {{ $t('ui.copy') }} </Button>
        </div>
        <p v-else class="text-sm text-muted-foreground"> {{ $t('ui.afterLoggingInAndPassingTheRegistrationReviewYourTeam') }} </p>
    </section>
  </div>
</template>
