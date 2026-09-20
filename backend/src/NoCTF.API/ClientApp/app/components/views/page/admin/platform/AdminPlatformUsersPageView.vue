<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformUsersPageViewState } from '~/features/routes/admin/platform/useAdminPlatformUsersPage'

const viewProps = defineProps<{ state: AdminPlatformUsersPageViewState }>()
const { Copy, KeyRound, LogIn, Plus, Trash2, currentUser, loading, pageLoading, loadError, search, roleFilter, ROLE_LABELS, STATUS_LABELS, MANAGED_ACCOUNT_STATUS_OPTIONS, REFERENCE_LABELS, filteredUsers, page, pageCount, total, pageLimit, loadPage, setPageSize, createBotOpen, creatingBot, botName, botRole, openCreateBot, setCreateBotOpen, createBot, detailOpen, detailLoading, detail, pendingRole, pendingAccountStatus, pendingEmailVerification, roleSaving, accountStatusSaving, emailVerificationSaving, invalidating, tokenOpen, tokenIntent, identitySwitchActive, tokenIssuing, tokenExpiresInSeconds, issuedToken, openToken, setTokenOpen, issueToken, copyIssuedToken, openDetail, saveRole, saveAccountStatus, saveEmailVerification, invalidateTokens, deleteOpen, previewLoading, preview, deletionMode, deletionReason, deleting, startDelete, confirmDelete, PrivateAccountPanel, AdminDateTime } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex min-w-0 flex-col gap-6">
    <div class="flex flex-wrap items-center gap-3">
      <Input v-model="search" class="w-full sm:max-w-sm" :placeholder="$t('ui.searchUsernameOrEmail')" :aria-label="$t('ui.searchUsernameOrEmail')" />
      <Select v-model="roleFilter">
        <SelectTrigger class="w-full sm:w-44" :aria-label="$t('ui.role')">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectItem value="all">{{ $t('ui.allRoles') }}</SelectItem>
            <SelectItem value="User">{{ $t('ui.user') }}</SelectItem>
            <SelectItem value="Organizer">{{ $t('ui.organizer') }}</SelectItem>
            <SelectItem value="Administrator">{{ $t('ui.administrator') }}</SelectItem>
            <SelectItem value="Bot">{{ $t('ui.bot') }}</SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
      <Button type="button" @click="openCreateBot">
        <Plus data-icon="inline-start" /> {{ $t('ui.createBot') }}
      </Button>
      <p class="text-sm text-muted-foreground sm:ml-auto">{{ $t('ui.showingUsers', { count: filteredUsers.length }) }}</p>
    </div>

    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ $message(loadError) }}</AlertDescription>
    </Alert>

    <div v-if="loading" class="flex flex-col gap-3">
      <Skeleton v-for="i in 6" :key="i" class="h-14 w-full" />
    </div>

    <Empty v-else-if="filteredUsers.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('ui.noMatchingUser') }}</EmptyTitle>
        <EmptyDescription>{{ $t('ui.adjustYourSearchOrFilters') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Table v-else class="min-w-[860px] [&_th]:px-4 [&_th]:py-3 [&_td]:px-4 [&_td]:py-4">
      <TableHeader class="bg-muted/30">
        <TableRow>
          <TableHead class="w-[26%]">{{ $t('ui.username') }}</TableHead>
          <TableHead class="w-[28%]">{{ $t('ui.email') }}</TableHead>
          <TableHead>{{ $t('ui.type') }}</TableHead>
          <TableHead>{{ $t('ui.role') }}</TableHead>
          <TableHead>{{ $t('ui.status') }}</TableHead>
          <TableHead>{{ $t('ui.registrationTime') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow
          v-for="user in filteredUsers"
          :key="user.id"
          class="cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset"
          role="button"
          tabindex="0"
          :aria-label="$t('ui.viewDetailsForUser', { name: user.userName ?? '' })"
          @click="openDetail(user)"
          @keydown.enter="openDetail(user)"
          @keydown.space.prevent="openDetail(user)"
        >
          <TableCell class="max-w-sm whitespace-normal break-words font-medium">
            {{ user.userName }}
            <Badge v-if="user.id === currentUser?.userId" variant="outline" class="ml-2">{{ $t('ui.me') }}</Badge>
          </TableCell>
          <TableCell class="max-w-sm whitespace-normal break-all text-muted-foreground">{{ user.email || $t('ui.symbol') }}</TableCell>
          <TableCell>
            <Badge :variant="user.kind === 'Bot' ? 'secondary' : 'outline'">
              {{ user.kind === 'Bot' ? $t('ui.bot') : $t('ui.user') }}
            </Badge>
          </TableCell>
          <TableCell>
            <Badge :variant="user.role === 'Administrator' ? 'default' : 'secondary'">
              {{ ROLE_LABELS[String(user.role)] ? $t(ROLE_LABELS[String(user.role)]!) : user.role }}
            </Badge>
          </TableCell>
          <TableCell>
            <Badge :variant="user.accountStatus === 'Active' ? 'outline' : 'destructive'">
              {{ STATUS_LABELS[String(user.accountStatus)] ? $t(STATUS_LABELS[String(user.accountStatus)]!) : user.accountStatus }}
            </Badge>
          </TableCell>
          <TableCell>
            <component :is="AdminDateTime" :value="user.createdAt" />
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>

    <OffsetPagination
      v-if="filteredUsers.length > 0 || total > 0"
      :page="page"
      :page-count="pageCount"
      :total="total"
      :limit="pageLimit"
      :loading="pageLoading"
      @update:page="loadPage"
      @update:limit="setPageSize"
    />

    <Dialog :open="createBotOpen" @update:open="setCreateBotOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('ui.createBot') }}</DialogTitle>
          <DialogDescription>{{ $t('ui.botIsASpecialServiceAccountForWhichAccessTokens') }}</DialogDescription>
        </DialogHeader>
        <UiForm validation="feature" class="flex flex-col gap-4" @submit.prevent="createBot">
          <FieldGroup>
            <Field>
              <FieldLabel for="bot-name">{{ $t('ui.name') }}</FieldLabel>
              <Input id="bot-name" v-model="botName" required maxlength="50" :placeholder="$t('ui.forExampleScoreboardSync')" />
            </Field>
            <Field>
              <FieldLabel for="bot-role">{{ $t('ui.role') }}</FieldLabel>
              <Select v-model="botRole">
                <SelectTrigger id="bot-role" class="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="User">{{ $t('ui.user') }}</SelectItem>
                    <SelectItem value="Organizer">{{ $t('ui.organizer') }}</SelectItem>
                    <SelectItem value="Administrator">{{ $t('ui.administrator') }}</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button type="button" variant="outline" :disabled="creatingBot" @click="setCreateBotOpen(false)">
              {{ $t('ui.cancel') }}
            </Button>
            <Button type="submit" :disabled="creatingBot || !botName.trim()">
              <Spinner v-if="creatingBot" data-icon="inline-start" /> {{ $t('ui.create') }}
            </Button>
          </DialogFooter>
        </UiForm>
      </DialogContent>
    </Dialog>

    <Sheet v-model:open="detailOpen">
      <SheetContent class="gap-0 overflow-hidden data-[side=right]:w-full data-[side=right]:sm:max-w-2xl">
        <SheetHeader class="shrink-0 border-b px-6 py-5 pr-14">
          <SheetTitle>{{ $t('ui.userDetails') }}</SheetTitle>
          <SheetDescription>{{ detail?.userName ?? $t('ui.loading') }}</SheetDescription>
        </SheetHeader>
        <div v-if="detailLoading" class="flex flex-col gap-3 p-6">
          <Skeleton v-for="i in 5" :key="i" class="h-8 w-full" />
        </div>
        <ScrollSurface as="div" v-else-if="detail" :key="detail.id" class="flex min-h-0 flex-1 flex-col gap-6 overflow-y-auto p-6">
          <section class="flex flex-col gap-4" aria-labelledby="user-account-overview">
            <h3 id="user-account-overview" class="font-semibold">{{ $t('ui.accountInformation') }}</h3>
            <dl class="grid grid-cols-[6rem_minmax(0,1fr)] gap-x-5 gap-y-2.5 text-sm sm:grid-cols-[7rem_minmax(0,1fr)]">
              <dt class="text-muted-foreground">{{ $t('ui.userId') }}</dt>
              <dd class="select-all break-all font-mono">{{ detail.id }}</dd>
              <dt class="text-muted-foreground">{{ $t('ui.username') }}</dt>
              <dd class="break-words font-medium">{{ detail.userName }}</dd>
              <dt class="text-muted-foreground">{{ $t('ui.email') }}</dt>
              <dd class="break-all">
                {{ detail.email || $t('ui.symbol') }}
                <Badge v-if="detail.emailVerified" variant="secondary" class="ml-1">{{ $t('ui.verified') }}</Badge>
                <Badge v-else variant="outline" class="ml-1">{{ $t('ui.notVerified') }}</Badge>
              </dd>
              <dt class="text-muted-foreground">{{ $t('ui.type') }}</dt>
              <dd>{{ detail.kind === 'Bot' ? $t('ui.bot') : $t('ui.user') }}</dd>
              <dt class="text-muted-foreground">{{ $t('ui.status') }}</dt>
              <dd>{{ STATUS_LABELS[String(detail.accountStatus)] ? $t(STATUS_LABELS[String(detail.accountStatus)]!) : detail.accountStatus }}</dd>
              <dt class="text-muted-foreground">{{ $t('ui.tokenVersion') }}</dt>
              <dd class="font-mono tabular-nums">{{ detail.tokenVersion ?? 0 }}</dd>
              <dt class="text-muted-foreground">{{ $t('ui.registrationTime') }}</dt>
              <dd><component :is="AdminDateTime" :value="detail.createdAt" /></dd>
              <dt class="text-muted-foreground">{{ $t('ui.updateTime') }}</dt>
              <dd><component :is="AdminDateTime" :value="detail.updatedAt" /></dd>
            </dl>
          </section>

          <component :is="PrivateAccountPanel" v-if="detail.id" :key="detail.id" :user-id="detail.id" :show-activities="false" />

          <Separator />

          <section class="flex flex-col gap-4" aria-labelledby="user-account-management">
            <h3 id="user-account-management" class="font-semibold">{{ $t('ui.rolesAndAccountManagement') }}</h3>
            <FieldGroup>
              <Field>
                <FieldLabel for="user-role">{{ $t('ui.platformRole') }}</FieldLabel>
                <div class="flex items-center gap-2">
                  <Select v-model="pendingRole" :disabled="detail.id === currentUser?.userId">
                    <SelectTrigger id="user-role" class="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="User">{{ $t('ui.user') }}</SelectItem>
                        <SelectItem value="Organizer">{{ $t('ui.organizer') }}</SelectItem>
                        <SelectItem value="Administrator">{{ $t('ui.administrator') }}</SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                  <Button
                    :disabled="roleSaving || pendingRole === String(detail.role ?? 'User') || detail.id === currentUser?.userId"
                    @click="saveRole"
                  >
                    <Spinner v-if="roleSaving" data-icon="inline-start" /> {{ $t('ui.save') }} </Button>
                </div>
                <FieldDescription v-if="detail.id === currentUser?.userId">{{ $t('ui.youCannotModifyYourRole') }}</FieldDescription>
              </Field>
              <Field>
                <FieldLabel for="user-account-status">{{ $t('ui.accountStatus') }}</FieldLabel>
                <div class="flex items-center gap-2">
                  <Select
                    v-model="pendingAccountStatus"
                    :disabled="detail.id === currentUser?.userId || detail.accountStatus === 'Anonymized'"
                  >
                    <SelectTrigger id="user-account-status" class="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem
                          v-for="option in MANAGED_ACCOUNT_STATUS_OPTIONS"
                          :key="option.value"
                          :value="option.value"
                        >
                          {{ $t(option.label) }}
                        </SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                  <Button
                    :disabled="accountStatusSaving
                      || pendingAccountStatus === detail.accountStatus
                      || detail.id === currentUser?.userId
                      || detail.accountStatus === 'Anonymized'"
                    @click="saveAccountStatus"
                  >
                    <Spinner v-if="accountStatusSaving" data-icon="inline-start" />
                    {{ $t('ui.save') }}
                  </Button>
                </div>
                <FieldDescription v-if="detail.id === currentUser?.userId">
                  {{ $t('ui.youCannotChangeYourOwnAccountStatus') }}
                </FieldDescription>
                <FieldDescription v-else-if="detail.accountStatus === 'Anonymized'">
                  {{ $t('ui.anAnonymizedAccountCannotBeChanged') }}
                </FieldDescription>
                <FieldDescription v-else>
                  {{ $t('ui.changingTheAccountStatusRevokesTheUserSExistingAccess') }}
                </FieldDescription>
              </Field>
              <Field>
                <FieldLabel for="user-email-verification">{{ $t('ui.emailActivationStatus') }}</FieldLabel>
                <div class="flex items-center gap-2">
                  <Select
                    v-model="pendingEmailVerification"
                    :disabled="detail.accountStatus === 'Anonymized'"
                  >
                    <SelectTrigger id="user-email-verification" class="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="Verified">{{ $t('ui.activated') }}</SelectItem>
                        <SelectItem value="Unverified">{{ $t('ui.notActivated') }}</SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                  <Button
                    :disabled="emailVerificationSaving
                      || (pendingEmailVerification === 'Verified') === detail.emailVerified
                      || detail.accountStatus === 'Anonymized'"
                    @click="saveEmailVerification"
                  >
                    <Spinner v-if="emailVerificationSaving" data-icon="inline-start" />
                    {{ $t('ui.save') }}
                  </Button>
                </div>
                <FieldDescription v-if="detail.accountStatus === 'Anonymized'">
                  {{ $t('ui.anAnonymizedAccountCannotBeChanged') }}
                </FieldDescription>
                <FieldDescription v-else>
                  {{ $t('ui.changingEmailActivationStatusInvalidatesTheUserSExistingAccess') }}
                </FieldDescription>
              </Field>
            </FieldGroup>
          </section>

          <Separator />

          <section class="flex flex-col gap-4" aria-labelledby="user-issued-access">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <h3 id="user-issued-access" class="font-semibold">{{ $t('ui.administratorIssuedAccess') }}</h3>
              <div class="flex flex-wrap gap-2">
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  :disabled="detail.accountStatus !== 'Active'"
                  @click="openToken(detail, 'issue')"
                >
                  <KeyRound data-icon="inline-start" />{{ $t('ui.issueJwt') }}
                </Button>
                <Button
                  type="button"
                  size="sm"
                  :disabled="detail.accountStatus !== 'Active' || identitySwitchActive"
                  @click="openToken(detail, 'impersonate')"
                >
                  <LogIn data-icon="inline-start" />{{ $t('ui.signInAsThisUser') }}
                </Button>
              </div>
            </div>
            <p v-if="detail.accountStatus !== 'Active'" class="text-sm text-muted-foreground">
              {{ $t('ui.onlyActiveAccountsCanReceiveAdministratorIssuedTokens') }}
            </p>
            <FieldDescription>{{ $t('ui.issuedTokensAreStatelessAndCanOnlyBeRevokedTogether') }}</FieldDescription>
          </section>

          <Separator />

          <div class="flex flex-col gap-3">
            <h3 class="text-sm font-medium">{{ $t('ui.dangerousOperation') }}</h3>
            <div class="flex flex-wrap gap-2">
              <AlertDialog>
                <AlertDialogTrigger as-child>
                  <Button variant="outline">
                    <KeyRound data-icon="inline-start" /> {{ $t('ui.revokeAllTokens') }} </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>{{ $t('ui.revokeToken') }}</AlertDialogTitle>
                    <AlertDialogDescription>
                      {{ $t('ui.revokeAllAccessAndRefreshTokensForTheUserMust', { user: detail.userName ?? '-' }) }}
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel>
                    <AlertDialogAction :disabled="invalidating" @click="invalidateTokens">
                      <Spinner v-if="invalidating" data-icon="inline-start" /> {{ $t('ui.confirmRevocation') }} </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
              <Button variant="destructive" @click="startDelete">
                <Trash2 data-icon="inline-start" /> {{ $t('ui.deleteUser') }} </Button>
            </div>
          </div>
        </ScrollSurface>
      </SheetContent>
    </Sheet>

    <Dialog :open="tokenOpen" @update:open="setTokenOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {{ tokenIntent === 'impersonate' ? $t('ui.signInAsThisUser') : $t('ui.issueAccessToken') }}
          </DialogTitle>
          <DialogDescription>
            {{ tokenIntent === 'impersonate'
              ? $t('ui.impersonationTokenDescription', { user: detail?.userName ?? '-' })
              : $t('ui.issueUserAccessTokenFor', { user: detail?.userName ?? '-' }) }}
          </DialogDescription>
        </DialogHeader>
        <template v-if="!issuedToken">
          <UiForm validation="feature" class="flex flex-col gap-4" @submit.prevent="issueToken">
            <FieldGroup>
              <Field>
                <FieldLabel for="user-token-ttl">{{ $t('ui.validityPeriodSeconds') }}</FieldLabel>
                <NumberInput id="user-token-ttl" v-model.number="tokenExpiresInSeconds" min="60" max="31536000" step="60" required />
                <FieldDescription>{{ $t('ui.tokenLifetimeRange') }}</FieldDescription>
              </Field>
            </FieldGroup>
            <DialogFooter>
              <Button type="button" variant="outline" @click="setTokenOpen(false)">{{ $t('ui.cancel') }}</Button>
              <Button type="submit" :disabled="tokenIssuing || tokenExpiresInSeconds < 60 || tokenExpiresInSeconds > 31536000">
                <Spinner v-if="tokenIssuing" data-icon="inline-start" />
                {{ tokenIntent === 'impersonate' ? $t('ui.issueAndSignIn') : $t('ui.issue') }}
              </Button>
            </DialogFooter>
          </UiForm>
        </template>
        <template v-else>
          <Alert>
            <AlertDescription>{{ $t('ui.theTokenIsOnlyDisplayedOncePleaseCopyAndSave') }}</AlertDescription>
          </Alert>
          <FieldGroup>
            <Field>
              <FieldLabel for="issued-user-token">{{ $t('ui.accessToken') }}</FieldLabel>
              <div class="flex items-center gap-2">
                <Input id="issued-user-token" :model-value="issuedToken.accessToken" readonly class="font-mono text-xs" />
                <Button type="button" size="icon" variant="outline" :aria-label="$t('ui.copyToken')" @click="copyIssuedToken">
                  <Copy />
                </Button>
              </div>
              <FieldDescription>
                {{ $t('ui.expirationDate') }} <component :is="AdminDateTime" :value="issuedToken.expiresAt" />
              </FieldDescription>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button type="button" @click="setTokenOpen(false)">{{ $t('ui.iHaveSavedClose') }}</Button>
          </DialogFooter>
        </template>
      </DialogContent>
    </Dialog>

    <AlertDialog v-model:open="deleteOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('ui.deleteUser2', { user: detail?.userName ?? '-' }) }}</AlertDialogTitle>
          <AlertDialogDescription> {{ $t('ui.deletionIsIrreversiblePleaseConfirmTheScopeOfImpactFirst') }} </AlertDialogDescription>
        </AlertDialogHeader>
        <div v-if="previewLoading" class="flex flex-col gap-2">
          <Skeleton v-for="i in 3" :key="i" class="h-8 w-full" />
        </div>
        <div v-else-if="preview" class="flex flex-col gap-4">
          <Alert v-if="preview.selfDeletionForbidden || preview.lastAdministratorProtected" variant="destructive">
            <AlertDescription>
              {{ preview.selfDeletionForbidden ? $t('ui.youCannotDeleteYourOwnAccount') : $t('ui.theLastAdministratorCannotBeDeleted') }}
            </AlertDescription>
          </Alert>
          <div v-if="preview.references?.length" class="flex flex-col gap-2">
            <p class="text-sm text-muted-foreground">{{ $t('ui.thisUserHasTheFollowingBusinessReferences') }}</p>
            <div class="flex flex-wrap gap-2">
              <Badge v-for="reference in preview.references" :key="reference.code" variant="secondary">
                {{ REFERENCE_LABELS[reference.code ?? ''] ? $t(REFERENCE_LABELS[reference.code ?? '']!) : reference.code }} × {{ reference.count }}
              </Badge>
            </div>
          </div>
          <p v-else class="text-sm text-muted-foreground">{{ $t('ui.thisUserHasNoBusinessReferences') }}</p>
          <FieldGroup>
            <Field>
              <FieldLabel for="deletion-mode">{{ $t('ui.deleteMethod') }}</FieldLabel>
              <Select v-model="deletionMode">
                <SelectTrigger id="deletion-mode" class="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem v-if="preview.canAnonymize" value="Anonymize">{{ $t('ui.anonymizeKeepDataRemoveIdentity') }}</SelectItem>
                    <SelectItem v-if="preview.canHardDelete" value="HardDelete">{{ $t('ui.physicalDeletionCompleteErasure') }}</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="deletion-reason">{{ $t('ui.reasonForDeletion') }}</FieldLabel>
              <Input id="deletion-reason" v-model="deletionReason" required :placeholder="$t('ui.willBeLoggedToTheAuditLog')" />
            </Field>
          </FieldGroup>
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            :disabled="deleting || !preview || !deletionReason.trim()
              || preview.selfDeletionForbidden || preview.lastAdministratorProtected"
            @click="confirmDelete"
          >
            <Spinner v-if="deleting" data-icon="inline-start" /> {{ $t('ui.confirmDeletion') }} </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
