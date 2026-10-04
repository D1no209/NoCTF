<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdPermissionsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdPermissionsPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdPermissionsPageViewState }>()
const { adminUserPath, X, canManagePermissions, permissions, candidates, loading, error, candidateName, search, filteredCandidates, assigned, add, remove, saving, save, transferTarget, transferConfirm, transferring, transfer, roles, onClickTransferConfirm } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="!canManagePermissions">
      <AlertDescription>{{ $t('administration.competitionsBy.description.competitionLeaderPlatformAdministrator') }}</AlertDescription>
    </Alert>
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>
    <Skeleton v-if="loading" class="h-64 w-full" />

    <template v-else-if="permissions">
      <Card class="gap-0">
        <section id="competition-collaboration-permissions" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
        <CardHeader>
          <CardTitle>{{ $t('administration.label.collaborationPermissions') }}</CardTitle>
          <CardDescription>
            <span>{{ $t('common.label.competitionLeader') }}: </span><NuxtLink v-if="permissions.ownerId" :to="adminUserPath(permissions.ownerId)" class="hover:underline">{{ candidateName(permissions.ownerId) }}</NuxtLink>
          </CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-6">
          <div v-for="r in roles" :key="r.key" class="flex flex-col gap-2">
            <h3 class="text-sm font-medium">{{ translate(r.label) }}</h3>
            <div class="flex flex-wrap items-center gap-2">
              <Badge v-for="id in r.list.value" :key="id" variant="secondary" class="gap-1">
                <NuxtLink :to="adminUserPath(id)" class="hover:underline">{{ candidateName(id) }}</NuxtLink>
                <ActionButton
                  v-if="canManagePermissions"
                  type="button"
                  class="inline-flex"
                  :aria-label="$t('administration.label.remove', { user: candidateName(id) })"
                  @click="remove(r.key, id)"
                >
                  <X class="size-3" />
                </ActionButton>
              </Badge>
              <span v-if="r.list.value.length === 0" class="text-sm text-muted-foreground">{{ $t('administration.label.none') }}</span>
            </div>
          </div>

          <template v-if="canManagePermissions">
            <Separator />
            <Field>
              <FieldLabel for="candidate-search">{{ $t('administration.competitionsBy.description.addCollaborationMembersSearch') }}</FieldLabel>
              <Input id="candidate-search" v-model="search" :placeholder="$t('administration.label.searchUsername')" />
            </Field>
            <div class="flex flex-col gap-1">
              <div
                v-for="c in filteredCandidates"
                :key="c.id"
                class="flex items-center justify-between gap-2 rounded-md border px-3 py-2 text-sm"
              >
                <span>
                  <NuxtLink :to="adminUserPath(c.id)" class="hover:underline">{{ c.userName }}</NuxtLink>
                  <span v-if="!c.emailVerified" class="text-muted-foreground">{{ $t('administration.label.emailVerified') }}</span>
                </span>
                <div class="flex items-center gap-1">
                  <template v-if="!assigned(c.id)">
                    <Button variant="ghost" size="sm" @click="add('manager', c.id)">{{ $t('administration.label.setAdministrator') }}</Button>
                    <Button variant="ghost" size="sm" @click="add('judge', c.id)">{{ $t('administration.label.setReferee') }}</Button>
                    <Button variant="ghost" size="sm" @click="add('observer', c.id)">{{ $t('administration.label.setObserver') }}</Button>
                  </template>
                  <Badge v-else variant="outline">{{ $t('administration.label.assigned') }}</Badge>
                </div>
              </div>
              <p v-if="filteredCandidates.length === 0" class="text-sm text-muted-foreground">{{ $t('administration.label.matchingCandidateUsers') }}</p>
            </div>
            <div>
              <Button :disabled="saving" @click="save">
                <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('administration.label.savePermissionChanges') }} </Button>
            </div>
          </template>
        </CardContent>
        </section>

        <template v-if="canManagePermissions">
        <Separator />
        <section id="competition-ownership-transfer" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
        <CardHeader>
          <CardTitle class="text-destructive">{{ $t('administration.label.transferOwnership') }}</CardTitle>
          <CardDescription>{{ $t('administration.competitionsBy.description.transferIdentityCompetitionLeader') }}</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-wrap items-center gap-2">
          <Select v-model="transferTarget">
            <SelectTrigger class="w-64">
              <SelectValue :placeholder="$t('administration.competitionsBy.description.chooseNewPersonCharge')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="c in candidates.filter(c => c.id && c.id !== permissions?.ownerId)" :key="c.id" :value="c.id!">
                  {{ c.userName }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Button variant="destructive" :disabled="!transferTarget" @click="onClickTransferConfirm(true)"> {{ $t('administration.label.transferOwnership') }} </Button>
        </CardContent>
        </section>
        </template>
      </Card>
    </template>

    <AlertDialog v-model:open="transferConfirm">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('administration.label.transferOwnership') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('administration.competitionsBy.description.transferCompetitionOwnershipTakes', { user: candidateName(transferTarget) }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <AlertDialogAction variant="destructive" :disabled="transferring" @click="transfer">
            <Spinner v-if="transferring" data-icon="inline-start" /> {{ $t('common.label.confirmTransfer') }} </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
