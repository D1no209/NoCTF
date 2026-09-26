<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdProgressionPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdProgressionPage'

const props = defineProps<{ state: AdminCompetitionsByIdProgressionPageViewState }>()
const {
  competition, canWrite, enabled, showPlayerMap, nodes, edges, badges,
  availableChallenges, filteredChallenges, challengeSearch, challengeDirection,
  challengeDirections, loading, saving, badgeSaving, error,
  selectedNode, selectedEdge, newBadgeName, newBadgeDescription, newBadgeImage,
  editingBadgeId, editBadgeName, editBadgeDescription,
  load, addChallenge, addBadge, connect, selectNode, selectEdge,
  removeSelectedNode, removeSelectedEdge, setEdgeCondition, save,
  onBadgeFileChange, createBadge, deleteBadge, ProgressionCanvas,
  onEditBadgeFileChange, beginEditBadge, updateBadge,
} = toRefs(props.state)
</script>

<template>
  <section class="flex flex-col gap-5">
    <header class="flex flex-wrap items-start justify-between gap-4 border-b pb-4">
      <div>
        <h2 class="text-xl font-semibold">{{ $t('progression.title') }}</h2>
        <p class="mt-1 text-sm text-muted-foreground">{{ $t('progression.description') }}</p>
      </div>
      <Button :disabled="!canWrite || saving || loading || competition?.mode !== 'Ctf'" @click="save">
        {{ saving ? $t('progression.saving') : $t('progression.save') }}
      </Button>
    </header>
    <Alert v-if="competition?.mode !== 'Ctf'"><AlertDescription>{{ $t('progression.ctfOnly') }}</AlertDescription></Alert>
    <Alert v-if="error" variant="destructive">
      <AlertDescription class="flex items-center justify-between gap-3">
        <span>{{ error }}</span><Button size="sm" variant="outline" @click="load">{{ $t('ui.retry') }}</Button>
      </AlertDescription>
    </Alert>
    <div v-if="loading" class="h-80 animate-pulse rounded-lg border bg-muted/30" />
    <template v-else-if="competition?.mode === 'Ctf'">
      <div class="grid gap-3 rounded-lg border bg-card p-4 sm:grid-cols-2">
        <Field orientation="horizontal"><Switch id="progression-enabled" v-model="enabled" :disabled="!canWrite" /><FieldLabel for="progression-enabled">{{ $t('progression.enable') }}</FieldLabel></Field>
        <Field orientation="horizontal"><Switch id="progression-show-map" v-model="showPlayerMap" :disabled="!canWrite" /><FieldLabel for="progression-show-map">{{ $t('progression.showMap') }}</FieldLabel></Field>
        <p class="text-xs text-muted-foreground sm:col-span-2">{{ $t('progression.saveWarning') }}</p>
      </div>

      <div class="grid gap-5 xl:grid-cols-[19rem_minmax(0,1fr)_18rem]">
        <aside class="space-y-5 rounded-lg border bg-card p-4">
          <div>
            <h3 class="font-semibold">{{ $t('progression.challengeCatalog') }}</h3>
            <p class="mb-3 text-xs text-muted-foreground">{{ $t('progression.challengeImportHint') }}</p>
            <div class="mb-3 grid gap-2">
              <Input v-model="challengeSearch" :placeholder="$t('progression.searchChallenges')" :aria-label="$t('progression.searchChallenges')" />
              <Select v-model="challengeDirection">
                <SelectTrigger class="w-full" :aria-label="$t('ui.category')"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="all">{{ $t('ui.allDirections') }}</SelectItem>
                    <SelectItem v-for="direction in challengeDirections" :key="direction.value" :value="direction.value">{{ direction.label }}</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </div>
            <ScrollSurface axis="y" class="max-h-[26rem]" :aria-label="$t('progression.challengeCatalog')">
              <Button v-for="challenge in filteredChallenges" :key="challenge.id"
                type="button" variant="ghost" :disabled="!canWrite"
                class="flex w-full items-center justify-between gap-2 px-2 text-left text-sm"
                @click="addChallenge(challenge)">
                <span class="truncate">{{ challenge.customTitle || challenge.title }}</span>
                <span class="shrink-0 text-xs text-muted-foreground">+</span>
              </Button>
              <p v-if="!availableChallenges.length" class="text-xs text-muted-foreground">{{ $t('progression.noChallenges') }}</p>
              <p v-else-if="!filteredChallenges.length" class="text-xs text-muted-foreground">{{ $t('progression.noMatchingChallenges') }}</p>
            </ScrollSurface>
          </div>
          <div class="border-t pt-4">
            <h3 class="font-semibold">{{ $t('progression.badgeCatalog') }}</h3>
            <p class="mb-3 text-xs text-muted-foreground">{{ $t('progression.badgeImportHint') }}</p>
            <ScrollSurface axis="y" class="max-h-56" :aria-label="$t('progression.badgeCatalog')">
              <div v-for="badge in badges" :key="badge.id" class="flex items-center gap-2 rounded border p-2">
                <img :src="badge.imageUrl" :alt="badge.name" class="size-9 rounded object-cover" />
                <span class="min-w-0 flex-1 truncate text-sm">{{ badge.name }}</span>
                <Button type="button" size="icon-sm" variant="ghost" :disabled="!canWrite" :aria-label="$t('progression.importBadge')" @click="addBadge(badge)">+</Button>
                <Button type="button" size="icon-sm" variant="ghost" :disabled="!canWrite" :aria-label="$t('progression.editBadge')" @click="beginEditBadge(badge)">✎</Button>
                <Button type="button" size="icon-sm" variant="ghost" :disabled="!canWrite" :aria-label="$t('progression.deleteBadge')" @click="deleteBadge(badge.id!)">×</Button>
              </div>
            </ScrollSurface>
            <div v-if="editingBadgeId && canWrite" class="mt-3 space-y-2 rounded border p-2">
              <Input v-model="editBadgeName" maxlength="160" :placeholder="$t('progression.badgeName')" />
              <Textarea v-model="editBadgeDescription" maxlength="1000" :placeholder="$t('progression.badgeDescription')" rows="2" />
              <FileInput accept="image/png,image/jpeg,image/webp" class="w-full text-xs" :aria-label="$t('progression.badgeImage')" @change="onEditBadgeFileChange($event)" />
              <Button size="sm" :disabled="badgeSaving || !editBadgeName.trim()" @click="updateBadge">{{ $t('progression.updateBadge') }}</Button>
            </div>
            <div v-if="canWrite" class="mt-3 space-y-2 border-t pt-3">
              <Input v-model="newBadgeName" maxlength="160" :placeholder="$t('progression.badgeName')" />
              <Textarea v-model="newBadgeDescription" maxlength="1000" :placeholder="$t('progression.badgeDescription')" rows="2" />
              <FileInput accept="image/png,image/jpeg,image/webp" class="w-full text-xs" :aria-label="$t('progression.badgeImage')" @change="onBadgeFileChange($event)" />
              <Button size="sm" class="w-full" :disabled="badgeSaving || !newBadgeName.trim() || !newBadgeImage" @click="createBadge">
                {{ $t('progression.createBadge') }}
              </Button>
            </div>
          </div>
        </aside>

        <div class="relative min-h-[46rem] overflow-hidden rounded-lg border bg-card">
          <component :is="ProgressionCanvas" v-model:nodes="nodes" v-model:edges="edges"
            :read-only="!canWrite" height="46rem" @connect="connect" @node-select="selectNode" @edge-select="selectEdge" />
          <div v-if="!nodes.length" class="pointer-events-none absolute inset-0 flex items-center justify-center text-sm text-muted-foreground">
            {{ $t('progression.emptyCanvas') }}
          </div>
        </div>

        <aside class="rounded-lg border bg-card p-4">
          <h3 class="font-semibold">{{ $t('progression.inspector') }}</h3>
          <div v-if="selectedEdge" class="mt-4 space-y-4">
            <p class="text-sm">{{ $t('progression.predecessorCondition') }}</p>
            <Button size="sm" :variant="selectedEdge.data?.condition === 0 ? 'default' : 'outline'" :disabled="!canWrite" @click="setEdgeCondition(0)">{{ $t('progression.completed') }}</Button>
            <Button size="sm" :variant="selectedEdge.data?.condition === 1 ? 'default' : 'outline'" :disabled="!canWrite" @click="setEdgeCondition(1)">{{ $t('progression.incomplete') }}</Button>
            <Button v-if="canWrite" size="sm" variant="destructive" @click="removeSelectedEdge">{{ $t('progression.removeEdge') }}</Button>
          </div>
          <div v-else-if="selectedNode" class="mt-4 space-y-3">
            <p class="break-words text-sm font-medium">{{ selectedNode.data?.title }}</p>
            <p class="text-xs text-muted-foreground">{{ selectedNode.data?.kind === 0 ? $t('progression.challengeNode') : $t('progression.badgeNode') }}</p>
            <Button v-if="canWrite" size="sm" variant="destructive" @click="removeSelectedNode">{{ $t('progression.removeNode') }}</Button>
          </div>
          <p v-else class="mt-4 text-sm text-muted-foreground">{{ $t('progression.selectHint') }}</p>
          <p class="mt-6 text-xs leading-5 text-muted-foreground">{{ $t('progression.ruleHint') }}</p>
        </aside>
      </div>
    </template>
  </section>
</template>
