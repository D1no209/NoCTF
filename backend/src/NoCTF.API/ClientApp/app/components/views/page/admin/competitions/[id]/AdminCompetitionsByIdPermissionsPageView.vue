<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdPermissionsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdPermissionsPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdPermissionsPageViewState }>()
const { X, canManagePermissions, permissions, candidates, loading, error, candidateName, search, filteredCandidates, assigned, add, remove, saving, save, transferTarget, transferConfirm, transferring, transfer, roles, onClickTransferConfirm } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="!canManagePermissions">
      <AlertDescription>{{ $t('ui.onlyTheCompetitionLeaderOrPlatformAdministratorCanManagePermissions') }}</AlertDescription>
    </Alert>
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>
    <Skeleton v-if="loading" class="h-64 w-full" />

    <template v-else-if="permissions">
      <Card>
        <CardHeader>
          <CardTitle>{{ $t('ui.collaborationPermissions') }}</CardTitle>
          <CardDescription>
            {{ $t('ui.owner2', { owner: candidateName(permissions.ownerId ?? '') }) }}
          </CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-6">
          <div v-for="r in roles" :key="r.key" class="flex flex-col gap-2">
            <h3 class="text-sm font-medium">{{ $t(r.label) }}</h3>
            <div class="flex flex-wrap items-center gap-2">
              <Badge v-for="id in r.list.value" :key="id" variant="secondary" class="gap-1">
                {{ candidateName(id) }}
                <ActionButton
                  v-if="canManagePermissions"
                  type="button"
                  class="inline-flex"
                  :aria-label="$t('ui.remove2', { user: candidateName(id) })"
                  @click="remove(r.key, id)"
                >
                  <X class="size-3" />
                </ActionButton>
              </Badge>
              <span v-if="r.list.value.length === 0" class="text-sm text-muted-foreground">{{ $t('ui.none2') }}</span>
            </div>
          </div>

          <template v-if="canManagePermissions">
            <Separator />
            <Field>
              <FieldLabel for="candidate-search">{{ $t('ui.addCollaborationMembersSearchCandidateUsers') }}</FieldLabel>
              <Input id="candidate-search" v-model="search" :placeholder="$t('ui.searchByUsername')" />
            </Field>
            <div class="flex flex-col gap-1">
              <div
                v-for="c in filteredCandidates"
                :key="c.id"
                class="flex items-center justify-between gap-2 rounded-md border px-3 py-2 text-sm"
              >
                <span>
                  {{ c.userName }}
                  <span v-if="!c.emailVerified" class="text-muted-foreground">{{ $t('ui.emailNotVerified') }}</span>
                </span>
                <div class="flex items-center gap-1">
                  <template v-if="!assigned(c.id)">
                    <Button variant="ghost" size="sm" @click="add('manager', c.id)">{{ $t('ui.setAsAdministrator') }}</Button>
                    <Button variant="ghost" size="sm" @click="add('judge', c.id)">{{ $t('ui.setAsReferee') }}</Button>
                    <Button variant="ghost" size="sm" @click="add('observer', c.id)">{{ $t('ui.setAsObserver') }}</Button>
                  </template>
                  <Badge v-else variant="outline">{{ $t('ui.assigned') }}</Badge>
                </div>
              </div>
              <p v-if="filteredCandidates.length === 0" class="text-sm text-muted-foreground">{{ $t('ui.noMatchingCandidateUsers') }}</p>
            </div>
            <div>
              <Button :disabled="saving" @click="save">
                <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('ui.savePermissionChanges') }} </Button>
            </div>
          </template>
        </CardContent>
      </Card>

      <Card v-if="canManagePermissions">
        <CardHeader>
          <CardTitle class="text-destructive">{{ $t('ui.transferOwnership') }}</CardTitle>
          <CardDescription>{{ $t('ui.transferTheIdentityOfTheCompetitionLeaderToAnotherUser') }}</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-wrap items-center gap-2">
          <Select v-model="transferTarget">
            <SelectTrigger class="w-64">
              <SelectValue :placeholder="$t('ui.chooseANewPersonInCharge')" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem v-for="c in candidates.filter(c => c.id && c.id !== permissions?.ownerId)" :key="c.id" :value="c.id!">
                  {{ c.userName }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
          <Button variant="destructive" :disabled="!transferTarget" @click="onClickTransferConfirm(true)"> {{ $t('ui.transferOwnership') }} </Button>
        </CardContent>
      </Card>
    </template>

    <AlertDialog v-model:open="transferConfirm">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('ui.transferOwnership') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('ui.transferCompetitionOwnershipToThisTakesEffectImmediately', { user: candidateName(transferTarget) }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel>
          <AlertDialogAction variant="destructive" :disabled="transferring" @click="transfer">
            <Spinner v-if="transferring" data-icon="inline-start" /> {{ $t('ui.confirmTransfer') }} </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
