<script setup lang="ts">
import { toRefs } from 'vue'
import type { AwdPanelViewState } from '~/features/challenges/panels/useAwdPanel'

const viewProps = defineProps<{ state: AwdPanelViewState }>()
const { emit, targets, targetsError, targetsLoaded, FlagSubmit, RuntimeAccessUrl, RuntimeCard, competition, challenge, flagDockTarget, runtimeDockTarget } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col divide-y">
    <component :is="RuntimeCard"
      class="pb-5"
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      controls="reset-only"
      :dock-target="runtimeDockTarget"
    />

    <section class="py-5" aria-labelledby="awd-targets-title">
      <h3 id="awd-targets-title" class="mb-4 text-sm font-semibold">{{ $t('challenges.label.attackTarget') }}</h3>
        <Skeleton v-if="!targetsLoaded" class="h-16 w-full" />
        <Alert v-else-if="targetsError">
          <AlertDescription>{{ $message(targetsError) }}</AlertDescription>
        </Alert>
        <Empty v-else-if="!targets.length" class="border py-8">
          <EmptyHeader>
            <EmptyTitle>{{ $t('challenges.awdPanel.label.targetAttackYet') }}</EmptyTitle>
          </EmptyHeader>
        </Empty>
        <Table v-else>
          <TableHeader>
            <TableRow>
              <TableHead>{{ $t('common.label.team') }}</TableHead>
              <TableHead>{{ $t('challenges.label.targetHost') }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="target in targets" :key="target.teamId">
              <TableCell class="font-medium">{{ target.teamName }}</TableCell>
              <TableCell>
                <div class="flex flex-col gap-1">
                  <component :is="RuntimeAccessUrl"
                    v-for="access in target.accesses ?? []"
                    :key="`${access.directAddress}:${access.webSocketAddress}`"
                    :access="access"
                  />
                </div>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
    </section>

    <component :is="FlagSubmit"
      :dock-target="flagDockTarget"
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      multiple
      :title="$t('challenges.label.batchSubmitFlag')"
      :maximum-attempts="challenge.maximumFlagAttempts"
      :remaining-attempts="challenge.remainingFlagAttempts"
      @submitted="emit('submitted')"
      @remaining-changed="emit('remainingChanged', $event)"
    />
  </div>
</template>
