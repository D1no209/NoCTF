<script setup lang="ts">
import { toRefs } from 'vue'
import type { TeamDetailDialogViewState } from '~/features/leaderboard/useTeamDetailDialog'

const viewProps = defineProps<{ state: TeamDetailDialogViewState }>()
const { Award, Medal, ShieldCheck, Swords, Target, Users, medalRankClass, normalizeChallengeKey, open, isAwdp, team, challengeTitle, solves, radarOption, challengePieOption, memberPieOption, hasCharts, ScoreTrendChart, entry, series, challenges, rangeStart, rangeEnd } = toRefs(viewProps.state)
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="max-h-[90vh] overflow-y-auto sm:max-w-4xl">
      <DialogHeader>
        <DialogTitle class="sr-only">{{ $t('ui.teamDetails') }}</DialogTitle>
      </DialogHeader>

      <div v-if="entry" class="flex flex-col gap-6">
        <div class="flex items-center gap-4">
          <Avatar class="size-14">
            <AvatarImage v-if="team?.avatarUrl" :src="team.avatarUrl" :alt="entry.teamName ?? ''" />
            <AvatarFallback class="text-lg">{{ entry.teamName?.slice(0, 2) ?? '?' }}</AvatarFallback>
          </Avatar>
          <div>
            <h2 class="text-display text-2xl">{{ entry.teamName }}</h2>
            <p class="text-sm text-muted-foreground">{{ $t('ui.registeredAt', { time: formatDateTime(team?.registeredAt) }) }}</p>
          </div>
        </div>

        <div class="grid grid-cols-2 gap-3" :class="isAwdp ? 'lg:grid-cols-6' : 'lg:grid-cols-4'">
          <Card>
            <CardContent class="flex items-center gap-3 pt-6">
              <Medal class="size-8" :class="medalRankClass[entry.rank ?? 0] ?? 'text-primary'" />
              <div>
                <p class="text-xs text-muted-foreground">{{ $t('ui.ranking2') }}</p>
                <p class="font-mono text-xl font-semibold tabular-nums">#{{ entry.rank }}</p>
              </div>
            </CardContent>
          </Card>
          <Card v-if="isAwdp">
            <CardContent class="flex items-center gap-3 pt-6">
              <Swords class="size-8 text-primary" />
              <div>
                <p class="text-xs text-muted-foreground">{{ $t('ui.attackScore') }}</p>
                <p class="font-mono text-xl font-semibold tabular-nums">{{ entry.attackScore ?? 0 }} {{ $t('ui.pts2') }}</p>
              </div>
            </CardContent>
          </Card>
          <Card v-if="isAwdp">
            <CardContent class="flex items-center gap-3 pt-6">
              <ShieldCheck class="size-8 text-emerald-600" />
              <div>
                <p class="text-xs text-muted-foreground">{{ $t('ui.defenseScore') }}</p>
                <p class="font-mono text-xl font-semibold tabular-nums">{{ entry.defenseScore ?? 0 }} {{ $t('ui.pts2') }}</p>
              </div>
            </CardContent>
          </Card>
          <Card v-if="!isAwdp">
            <CardContent class="flex items-center gap-3 pt-6">
              <Target class="size-8 text-primary" />
              <div>
                <p class="text-xs text-muted-foreground">{{ $t('ui.numberOfSolvedProblems') }}</p>
                <p class="font-mono text-xl font-semibold tabular-nums">{{ entry.solveCount ?? 0 }}</p>
              </div>
            </CardContent>
          </Card>
          <Card v-if="isAwdp">
            <CardContent class="flex items-center gap-3 pt-6">
              <Award class="size-8 text-destructive" />
              <div>
                <p class="text-xs text-muted-foreground">{{ $t('ui.penalty') }}</p>
                <p class="font-mono text-xl font-semibold tabular-nums">{{ entry.penaltyScore ?? 0 }} {{ $t('ui.pts2') }}</p>
              </div>
            </CardContent>
          </Card>
          <Card>
            <CardContent class="flex items-center gap-3 pt-6">
              <Award class="size-8 text-primary" />
              <div>
                <p class="text-xs text-muted-foreground">{{ $t('ui.totalScore') }}</p>
                <p class="font-mono text-xl font-semibold tabular-nums">{{ entry.score ?? 0 }} {{ $t('ui.pts2') }}</p>
              </div>
            </CardContent>
          </Card>
          <Card>
            <CardContent class="flex items-center gap-3 pt-6">
              <Users class="size-8 text-primary" />
              <div>
                <p class="text-xs text-muted-foreground">{{ $t('ui.numberOfMembers') }}</p>
                <p class="font-mono text-xl font-semibold tabular-nums">{{ team?.memberIds?.length ?? '-' }}</p>
              </div>
            </CardContent>
          </Card>
        </div>

        <Card v-if="isAwdp">
          <CardHeader>
            <CardTitle class="flex items-center gap-2 text-base">
              <Swords class="size-4" /> {{ $t('ui.challengeAttackAndDefenseScores') }}
            </CardTitle>
          </CardHeader>
          <CardContent>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{{ $t('ui.challenge') }}</TableHead>
                  <TableHead class="text-right">{{ $t('ui.attackScore') }}</TableHead>
                  <TableHead class="text-right">{{ $t('ui.defenseScore') }}</TableHead>
                  <TableHead class="text-right">{{ $t('ui.total') }}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="challenge in challenges" :key="challenge.competitionChallengeId">
                  <TableCell class="font-medium">{{ challenge.title }}</TableCell>
                  <TableCell class="text-right font-mono tabular-nums">
                    {{ entry.cells?.find(cell => normalizeChallengeKey(cell.competitionChallengeId) === normalizeChallengeKey(challenge.competitionChallengeId))?.attackScore ?? 0 }} {{ $t('ui.pts2') }}
                  </TableCell>
                  <TableCell class="text-right font-mono tabular-nums">
                    {{ entry.cells?.find(cell => normalizeChallengeKey(cell.competitionChallengeId) === normalizeChallengeKey(challenge.competitionChallengeId))?.defenseScore ?? 0 }} {{ $t('ui.pts2') }}
                  </TableCell>
                  <TableCell class="text-right font-mono font-semibold tabular-nums">
                    {{ (entry.cells?.find(cell => normalizeChallengeKey(cell.competitionChallengeId) === normalizeChallengeKey(challenge.competitionChallengeId))?.attackScore ?? 0) + (entry.cells?.find(cell => normalizeChallengeKey(cell.competitionChallengeId) === normalizeChallengeKey(challenge.competitionChallengeId))?.defenseScore ?? 0) }} {{ $t('ui.pts2') }}
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </CardContent>
        </Card>

        <div v-if="!isAwdp" class="grid gap-4 lg:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle class="flex items-center gap-2 text-base">
                <Target class="size-4" /> {{ $t('ui.problemSolvingDistribution') }} </CardTitle>
            </CardHeader>
            <CardContent>
              <MiniChart v-if="challenges.length" :option="radarOption" />
              <p v-else class="text-sm text-muted-foreground">{{ $t('ui.noDataYet') }}</p>
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle class="flex items-center gap-2 text-base">
                <Award class="size-4" /> {{ $t('ui.pointsChangeTrend') }} </CardTitle>
            </CardHeader>
            <CardContent>
              <component :is="ScoreTrendChart"
                :series="series ? [series] : []"
                :range-start="rangeStart"
                :range-end="rangeEnd"
                height="260px"
              />
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle class="flex items-center gap-2 text-base">
                <Award class="size-4" /> {{ $t('ui.questionScoreRatio') }} </CardTitle>
            </CardHeader>
            <CardContent>
              <MiniChart v-if="hasCharts" :option="challengePieOption" />
              <p v-else class="text-sm text-muted-foreground">{{ $t('ui.noScoreRecordYet') }}</p>
            </CardContent>
          </Card>
          <Card>
            <CardHeader>
              <CardTitle class="flex items-center gap-2 text-base">
                <Users class="size-4" /> {{ $t('ui.memberContributions') }} </CardTitle>
            </CardHeader>
            <CardContent>
              <MiniChart v-if="hasCharts" :option="memberPieOption" />
              <p v-else class="text-sm text-muted-foreground">{{ $t('ui.noScoreRecordYet') }}</p>
            </CardContent>
          </Card>
        </div>

        <Card v-if="!isAwdp">
          <CardHeader>
            <CardTitle class="text-base">{{ $t('ui.detailedRecords') }}</CardTitle>
          </CardHeader>
          <CardContent>
            <Tabs default-value="solves">
              <TabsList>
                <TabsTrigger value="solves">
                  <Target class="size-4" />
                  {{ $t('ui.solveHistory', { count: solves.length }) }}
                </TabsTrigger>
              </TabsList>
              <TabsContent value="solves">
                <ScrollArea class="h-72">
                  <ul class="flex flex-col gap-2 pr-4">
                    <li
                      v-for="(solve, index) in solves"
                      :key="index"
                      class="flex items-center justify-between gap-3 rounded-md border border-l-4 border-l-primary px-3 py-2"
                    >
                      <div class="flex flex-col gap-1">
                        <span class="flex items-center gap-2 font-medium">
                          <Badge variant="secondary" class="font-mono tabular-nums">#{{ solve.solveOrdinal ?? '-' }}</Badge>
                          {{ challengeTitle(solve.competitionChallengeId) }}
                        </span>
                        <span class="flex items-center gap-3 text-xs text-muted-foreground">
                          <span class="flex items-center gap-1">
                            <Users class="size-3" />
                            {{ solve.submitterName || $t('ui.unknownMember') }}
                          </span>
                          <span class="font-mono tabular-nums">{{ formatDateTime(solve.at) }}</span>
                        </span>
                      </div>
                      <Badge variant="secondary" class="font-mono tabular-nums">+{{ solve.points ?? 0 }} {{ $t('ui.pts2') }}</Badge>
                    </li>
                    <li v-if="!solves.length" class="py-8 text-center text-sm text-muted-foreground">{{ $t('ui.thereIsNoSolutionRecordYet') }}</li>
                  </ul>
                </ScrollArea>
              </TabsContent>
            </Tabs>
          </CardContent>
        </Card>
      </div>
    </DialogContent>
  </Dialog>
</template>
