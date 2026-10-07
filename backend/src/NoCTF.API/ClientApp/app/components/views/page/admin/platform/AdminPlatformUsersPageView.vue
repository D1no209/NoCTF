<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformUsersPageViewState } from '~/features/routes/admin/platform/useAdminPlatformUsersPage'

const viewProps = defineProps<{ state: AdminPlatformUsersPageViewState }>()
const { Copy, KeyRound, LogIn, Plus, Trash2, Unlink, currentUser, loading, pageLoading, loadError, search, roleFilter, ssoProviders, ssoProviderFilter, ROLE_LABELS, STATUS_LABELS, MANAGED_ACCOUNT_STATUS_OPTIONS, REFERENCE_LABELS, filteredUsers, page, pageCount, total, pageLimit, loadPage, setPageSize, createBotOpen, creatingBot, botName, botRole, openCreateBot, setCreateBotOpen, createBot, detailError, detailOpen, detailLoading, detail, pendingRole, pendingAccountStatus, pendingEmailVerification, roleSaving, accountStatusSaving, emailVerificationSaving, invalidating, ssoUnbinding, tokenOpen, tokenIntent, identitySwitchActive, tokenIssuing, tokenExpiresInSeconds, issuedToken, openToken, setTokenOpen, issueToken, copyIssuedToken, openDetail, saveRole, saveAccountStatus, saveEmailVerification, invalidateTokens, unbindManagedSsoIdentity, deleteOpen, previewLoading, preview, deletionMode, deletionReason, deleting, startDelete, confirmDelete, PrivateAccountPanel, AdminDateTime, MfaUserManagement } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex min-w-0 flex-col gap-6">
    <div class="flex flex-wrap items-center gap-3">
      <Input v-model="search" class="w-full sm:max-w-sm" :placeholder="$t('sso.adminUserSearchPlaceholder')" :aria-label="$t('sso.adminUserSearchPlaceholder')" />
      <Select v-model="roleFilter">
        <SelectTrigger class="w-full sm:w-44" :aria-label="$t('administration.label.role')">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectItem value="all">{{ $t('administration.label.roles') }}</SelectItem>
            <SelectItem value="User">{{ $t('administration.label.user') }}</SelectItem>
            <SelectItem value="Organizer">{{ $t('administration.label.organizer') }}</SelectItem>
            <SelectItem value="Administrator">{{ $t('administration.label.administrator') }}</SelectItem>
            <SelectItem value="Bot">{{ $t('administration.label.bot') }}</SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
      <Select v-model="ssoProviderFilter">
        <SelectTrigger class="w-full sm:w-52" :aria-label="$t('sso.identityProvider')">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectItem value="all">{{ $t('sso.allIdentityProviders') }}</SelectItem>
            <SelectItem v-for="provider in ssoProviders" :key="provider.id" :value="provider.id!">
              {{ provider.name }}
            </SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
      <Button type="button" @click="openCreateBot">
        <Plus data-icon="inline-start" /> {{ $t('administration.label.createBot') }}
      </Button>
      <p class="text-sm text-muted-foreground sm:ml-auto">{{ $t('administration.label.showingUsers', { count: filteredUsers.length }) }}</p>
    </div>

    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ $message(loadError) }}</AlertDescription>
    </Alert>

    <div v-if="loading" class="flex flex-col gap-3">
      <Skeleton v-for="i in 6" :key="i" class="h-14 w-full" />
    </div>

    <Empty v-else-if="filteredUsers.length === 0" class="border border-dashed py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('administration.label.matchingUser') }}</EmptyTitle>
        <EmptyDescription>{{ $t('administration.platformUsers.label.adjustSearchFilters') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Table v-else class="min-w-[1040px] [&_th]:px-4 [&_th]:py-3 [&_td]:px-4 [&_td]:py-4">
      <TableHeader class="bg-muted/30">
        <TableRow>
          <TableHead class="w-[26%]">{{ $t('common.label.username') }}</TableHead>
          <TableHead class="w-[28%]">{{ $t('common.label.email') }}</TableHead>
          <TableHead>{{ $t('common.label.type') }}</TableHead>
          <TableHead>{{ $t('administration.label.role') }}</TableHead>
          <TableHead>{{ $t('common.label.status') }}</TableHead>
          <TableHead>{{ $t('sso.externalIdentity') }}</TableHead>
          <TableHead>{{ $t('administration.label.registrationTime') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow
          v-for="user in filteredUsers"
          :key="user.id"
          class="cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset"
          role="button"
          tabindex="0"
          :aria-label="$t('administration.label.viewDetailsUser', { name: user.userName ?? '' })"
          @click="openDetail(user)"
          @keydown.enter="openDetail(user)"
          @keydown.space.prevent="openDetail(user)"
        >
          <TableCell class="max-w-sm whitespace-normal break-words font-medium">
            {{ user.userName }}
            <Badge v-if="user.id === currentUser?.userId" variant="outline" class="ml-2">{{ $t('administration.label.me') }}</Badge>
          </TableCell>
          <TableCell class="max-w-sm whitespace-normal break-all text-muted-foreground">{{ user.email || $t('common.label.symbol') }}</TableCell>
          <TableCell>
            <Badge :variant="user.kind === 'Bot' ? 'secondary' : 'outline'">
              {{ user.kind === 'Bot' ? $t('administration.label.bot') : $t('administration.label.user') }}
            </Badge>
          </TableCell>
          <TableCell>
            <Badge :variant="user.role === 'Administrator' ? 'default' : 'secondary'">
              {{ ROLE_LABELS[String(user.role)] ? translate(ROLE_LABELS[String(user.role)]!) : user.role }}
            </Badge>
          </TableCell>
          <TableCell>
            <Badge :variant="user.accountStatus === 'Active' ? 'outline' : 'destructive'">
              {{ STATUS_LABELS[String(user.accountStatus)] ? translate(STATUS_LABELS[String(user.accountStatus)]!) : user.accountStatus }}
            </Badge>
          </TableCell>
          <TableCell class="max-w-64 whitespace-normal">
            <div v-if="user.ssoBinding" class="flex flex-col gap-1">
              <span class="font-medium">{{ user.ssoBinding.providerName || user.ssoBinding.providerId }}</span>
              <span class="break-all text-xs text-muted-foreground">{{ user.ssoBinding.subject }}</span>
            </div>
            <span v-else class="text-muted-foreground">{{ $t('sso.notBound') }}</span>
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
          <DialogTitle>{{ $t('administration.label.createBot') }}</DialogTitle>
          <DialogDescription>{{ $t('administration.platformUsers.description.botSpecialServiceAccount') }}</DialogDescription>
        </DialogHeader>
        <UiForm validation="feature" class="flex flex-col gap-4" @submit.prevent="createBot">
          <FieldGroup>
            <Field>
              <FieldLabel for="bot-name">{{ $t('administration.label.name') }}</FieldLabel>
              <Input id="bot-name" v-model="botName" required maxlength="50" :placeholder="$t('administration.label.exampleScoreboardSync')" />
            </Field>
            <Field>
              <FieldLabel for="bot-role">{{ $t('administration.label.role') }}</FieldLabel>
              <Select v-model="botRole">
                <SelectTrigger id="bot-role" class="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="User">{{ $t('administration.label.user') }}</SelectItem>
                    <SelectItem value="Organizer">{{ $t('administration.label.organizer') }}</SelectItem>
                    <SelectItem value="Administrator">{{ $t('administration.label.administrator') }}</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button type="button" variant="outline" :disabled="creatingBot" @click="setCreateBotOpen(false)">
              {{ $t('common.action.cancel') }}
            </Button>
            <Button type="submit" :disabled="creatingBot || !botName.trim()">
              <Spinner v-if="creatingBot" data-icon="inline-start" /> {{ $t('common.action.create') }}
            </Button>
          </DialogFooter>
        </UiForm>
      </DialogContent>
    </Dialog>

    <Sheet v-model:open="detailOpen">
      <SheetContent class="gap-0 overflow-hidden data-[side=right]:w-full data-[side=right]:sm:max-w-2xl">
        <SheetHeader class="shrink-0 border-b px-6 py-5 pr-14">
          <SheetTitle>{{ $t('administration.label.userDetails') }}</SheetTitle>
          <SheetDescription>{{ detail?.userName ?? $t('common.label.loading') }}</SheetDescription>
        </SheetHeader>
        <div v-if="detailLoading" class="flex flex-col gap-3 p-6">
          <Skeleton v-for="i in 5" :key="i" class="h-8 w-full" />
        </div>
        <Alert v-else-if="detailError" variant="destructive" class="m-6"><AlertDescription>{{ $message(detailError) }}</AlertDescription></Alert>
        <ScrollSurface as="div" v-else-if="detail" :key="detail.id" class="flex min-h-0 flex-1 flex-col gap-6 overflow-y-auto p-6">
          <section class="flex flex-col gap-4" aria-labelledby="user-account-overview">
            <h3 id="user-account-overview" class="font-semibold">{{ $t('administration.label.accountInformation') }}</h3>
            <dl class="grid grid-cols-[6rem_minmax(0,1fr)] gap-x-5 gap-y-2.5 text-sm sm:grid-cols-[7rem_minmax(0,1fr)]">
              <dt class="text-muted-foreground">{{ $t('administration.label.userId') }}</dt>
              <dd class="select-all break-all font-mono">{{ detail.id }}</dd>
              <dt class="text-muted-foreground">{{ $t('common.label.username') }}</dt>
              <dd class="break-words font-medium">{{ detail.userName }}</dd>
              <dt class="text-muted-foreground">{{ $t('common.label.email') }}</dt>
              <dd class="break-all">
                {{ detail.email || $t('common.label.symbol') }}
                <Badge v-if="detail.emailVerified" variant="secondary" class="ml-1">{{ $t('common.label.verified.accountPanelView') }}</Badge>
                <Badge v-else variant="outline" class="ml-1">{{ $t('common.label.verified') }}</Badge>
              </dd>
              <dt class="text-muted-foreground">{{ $t('common.label.type') }}</dt>
              <dd>{{ detail.kind === 'Bot' ? $t('administration.label.bot') : $t('administration.label.user') }}</dd>
              <dt class="text-muted-foreground">{{ $t('common.label.status') }}</dt>
              <dd>{{ STATUS_LABELS[String(detail.accountStatus)] ? translate(STATUS_LABELS[String(detail.accountStatus)]!) : detail.accountStatus }}</dd>
              <dt class="text-muted-foreground">{{ $t('administration.label.tokenVersion') }}</dt>
              <dd class="font-mono tabular-nums">{{ detail.tokenVersion ?? 0 }}</dd>
              <dt class="text-muted-foreground">{{ $t('administration.label.registrationTime') }}</dt>
              <dd><component :is="AdminDateTime" :value="detail.createdAt" /></dd>
              <dt class="text-muted-foreground">{{ $t('administration.label.updateTime') }}</dt>
              <dd><component :is="AdminDateTime" :value="detail.updatedAt" /></dd>
            </dl>
          </section>

          <Separator />

          <section class="flex flex-col gap-4" aria-labelledby="user-sso-binding">
            <h3 id="user-sso-binding" class="font-semibold">{{ $t('sso.externalIdentity') }}</h3>
            <div v-if="detail.ssoBinding" class="flex flex-col gap-3 rounded-xl border p-4">
              <dl class="grid grid-cols-[7rem_minmax(0,1fr)] gap-x-4 gap-y-2 text-sm">
                <dt class="text-muted-foreground">{{ $t('sso.identityProvider') }}</dt>
                <dd>{{ detail.ssoBinding.providerName || detail.ssoBinding.providerId }}</dd>
                <dt class="text-muted-foreground">{{ $t('sso.protocol') }}</dt>
                <dd>{{ detail.ssoBinding.protocol }}</dd>
                <dt class="text-muted-foreground">{{ $t('sso.subject') }}</dt>
                <dd class="select-all break-all font-mono text-xs">{{ detail.ssoBinding.subject }}</dd>
                <dt class="text-muted-foreground">{{ $t('sso.boundAt') }}</dt>
                <dd><component :is="AdminDateTime" :value="detail.ssoBinding.boundAt" /></dd>
              </dl>
              <AlertDialog>
                <AlertDialogTrigger as-child>
                  <Button type="button" variant="destructive" size="sm" class="self-start" :disabled="ssoUnbinding">
                    <Spinner v-if="ssoUnbinding" data-icon="inline-start" />
                    <Unlink v-else data-icon="inline-start" />
                    {{ $t('sso.adminUnbind') }}
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>{{ $t('sso.adminUnbind') }}</AlertDialogTitle>
                    <AlertDialogDescription>{{ $t('sso.adminUnbindDescription') }}</AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel>
                    <AlertDialogAction @click="unbindManagedSsoIdentity">{{ $t('sso.adminUnbind') }}</AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            </div>
            <p v-else class="text-sm text-muted-foreground">{{ $t('sso.notBound') }}</p>
          </section>

          <component :is="MfaUserManagement" v-if="detail.id && detail.kind === 'Human'" :key="detail.id" :user-id="detail.id" :required="detail.mfaRequired ?? false" />
          <component :is="PrivateAccountPanel" v-if="detail.id" :key="detail.id" :user-id="detail.id" :show-activities="false" />

          <Separator />

          <section class="flex flex-col gap-4" aria-labelledby="user-account-management">
            <h3 id="user-account-management" class="font-semibold">{{ $t('administration.label.rolesAccountManagement') }}</h3>
            <FieldGroup>
              <Field>
                <FieldLabel for="user-role">{{ $t('administration.label.platformRole') }}</FieldLabel>
                <div class="flex items-center gap-2">
                  <Select v-model="pendingRole" :disabled="detail.id === currentUser?.userId">
                    <SelectTrigger id="user-role" class="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="User">{{ $t('administration.label.user') }}</SelectItem>
                        <SelectItem value="Organizer">{{ $t('administration.label.organizer') }}</SelectItem>
                        <SelectItem value="Administrator">{{ $t('administration.label.administrator') }}</SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                  <Button
                    :disabled="roleSaving || pendingRole === String(detail.role ?? 'User') || detail.id === currentUser?.userId"
                    @click="saveRole"
                  >
                    <Spinner v-if="roleSaving" data-icon="inline-start" /> {{ $t('common.action.save') }} </Button>
                </div>
                <FieldDescription v-if="detail.id === currentUser?.userId">{{ $t('administration.platformUsers.validation.modifyRoleFormat') }}</FieldDescription>
              </Field>
              <Field>
                <FieldLabel for="user-account-status">{{ $t('administration.label.accountStatus') }}</FieldLabel>
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
                          {{ translate(option.label) }}
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
                    {{ $t('common.action.save') }}
                  </Button>
                </div>
                <FieldDescription v-if="detail.id === currentUser?.userId">
                  {{ $t('administration.platformUsers.validation.changeOwnFormat') }}
                </FieldDescription>
                <FieldDescription v-else-if="detail.accountStatus === 'Anonymized'">
                  {{ $t('administration.platformUsers.validation.anonymizedAccountFormat.platformUsersPage') }}
                </FieldDescription>
                <FieldDescription v-else>
                  {{ $t('administration.platformUsers.description.changingAccountStatusRevokes') }}
                </FieldDescription>
              </Field>
              <Field>
                <FieldLabel for="user-email-verification">{{ $t('administration.label.emailActivationStatus') }}</FieldLabel>
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
                        <SelectItem value="Verified">{{ $t('administration.label.activated') }}</SelectItem>
                        <SelectItem value="Unverified">{{ $t('administration.label.activated.usersPageView') }}</SelectItem>
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
                    {{ $t('common.action.save') }}
                  </Button>
                </div>
                <FieldDescription v-if="detail.accountStatus === 'Anonymized'">
                  {{ $t('administration.platformUsers.validation.anonymizedAccountFormat.platformUsersPage') }}
                </FieldDescription>
                <FieldDescription v-else>
                  {{ $t('administration.platformUsers.description.changingEmailActivationStatus') }}
                </FieldDescription>
              </Field>
            </FieldGroup>
          </section>

          <Separator />

          <section class="flex flex-col gap-4" aria-labelledby="user-issued-access">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <h3 id="user-issued-access" class="font-semibold">{{ $t('administration.label.administratorIssuedAccess') }}</h3>
              <div class="flex flex-wrap gap-2">
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  :disabled="detail.accountStatus !== 'Active'"
                  @click="openToken(detail, 'issue')"
                >
                  <KeyRound data-icon="inline-start" />{{ $t('administration.label.issueJwt') }}
                </Button>
                <Button
                  type="button"
                  size="sm"
                  :disabled="detail.accountStatus !== 'Active' || identitySwitchActive"
                  @click="openToken(detail, 'impersonate')"
                >
                  <LogIn data-icon="inline-start" />{{ $t('administration.platformUsers.label.signUser') }}
                </Button>
              </div>
            </div>
            <p v-if="detail.accountStatus !== 'Active'" class="text-sm text-muted-foreground">
              {{ $t('administration.platformUsers.description.activeAccountsReceiveAdministrator') }}
            </p>
            <FieldDescription>{{ $t('administration.platformUsers.description.issuedTokensStatelessRevoked') }}</FieldDescription>
          </section>

          <Separator />

          <div class="flex flex-col gap-3">
            <h3 class="text-sm font-medium">{{ $t('administration.label.dangerous') }}</h3>
            <div class="flex flex-wrap gap-2">
              <AlertDialog>
                <AlertDialogTrigger as-child>
                  <Button variant="outline">
                    <KeyRound data-icon="inline-start" /> {{ $t('administration.label.revokeTokens') }} </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>{{ $t('administration.label.revokeToken') }}</AlertDialogTitle>
                    <AlertDialogDescription>
                      {{ $t('administration.platformUsers.validation.revokeAccessFormat', { user: detail.userName ?? '-' }) }}
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel>
                    <AlertDialogAction :disabled="invalidating" @click="invalidateTokens">
                      <Spinner v-if="invalidating" data-icon="inline-start" /> {{ $t('administration.label.confirmRevocation') }} </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
              <Button variant="destructive" @click="startDelete">
                <Trash2 data-icon="inline-start" /> {{ $t('administration.label.deleteUser') }} </Button>
            </div>
          </div>
        </ScrollSurface>
      </SheetContent>
    </Sheet>

    <Dialog :open="tokenOpen" @update:open="setTokenOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>
            {{ tokenIntent === 'impersonate' ? $t('administration.platformUsers.label.signUser') : $t('administration.label.issueAccessToken') }}
          </DialogTitle>
          <DialogDescription>
            {{ tokenIntent === 'impersonate'
              ? $t('administration.label.impersonationTokenDescription', { user: detail?.userName ?? '-' })
              : $t('administration.platformUsers.label.issueUserAccessToken', { user: detail?.userName ?? '-' }) }}
          </DialogDescription>
        </DialogHeader>
        <template v-if="!issuedToken">
          <UiForm validation="feature" class="flex flex-col gap-4" @submit.prevent="issueToken">
            <FieldGroup>
              <Field>
                <FieldLabel for="user-token-ttl">{{ $t('administration.label.validityPeriodSeconds') }}</FieldLabel>
                <NumberInput id="user-token-ttl" v-model.number="tokenExpiresInSeconds" min="60" max="31536000" step="60" required />
                <FieldDescription>{{ $t('administration.label.tokenLifetimeRange') }}</FieldDescription>
              </Field>
            </FieldGroup>
            <DialogFooter>
              <Button type="button" variant="outline" @click="setTokenOpen(false)">{{ $t('common.action.cancel') }}</Button>
              <Button type="submit" :disabled="tokenIssuing || tokenExpiresInSeconds < 60 || tokenExpiresInSeconds > 31536000">
                <Spinner v-if="tokenIssuing" data-icon="inline-start" />
                {{ tokenIntent === 'impersonate' ? $t('administration.label.issueSign') : $t('administration.label.issue') }}
              </Button>
            </DialogFooter>
          </UiForm>
        </template>
        <template v-else>
          <Alert>
            <AlertDescription>{{ $t('administration.platformUsers.description.tokenDisplayedOnceCopy') }}</AlertDescription>
          </Alert>
          <FieldGroup>
            <Field>
              <FieldLabel for="issued-user-token">{{ $t('administration.label.accessToken') }}</FieldLabel>
              <div class="flex items-center gap-2">
                <Input id="issued-user-token" :model-value="issuedToken.accessToken" readonly class="font-mono text-xs" />
                <Button type="button" size="icon" variant="outline" :aria-label="$t('administration.label.copyToken')" @click="copyIssuedToken">
                  <Copy />
                </Button>
              </div>
              <FieldDescription>
                {{ $t('administration.label.expirationDate') }} <component :is="AdminDateTime" :value="issuedToken.expiresAt" />
              </FieldDescription>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button type="button" @click="setTokenOpen(false)">{{ $t('administration.label.iSavedClose') }}</Button>
          </DialogFooter>
        </template>
      </DialogContent>
    </Dialog>

    <AlertDialog v-model:open="deleteOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('administration.label.deleteUser.usersPageView', { user: detail?.userName ?? '-' }) }}</AlertDialogTitle>
          <AlertDialogDescription> {{ $t('administration.platformUsers.description.deletionIrreversibleConfirmScope') }} </AlertDialogDescription>
        </AlertDialogHeader>
        <div v-if="previewLoading" class="flex flex-col gap-2">
          <Skeleton v-for="i in 3" :key="i" class="h-8 w-full" />
        </div>
        <div v-else-if="preview" class="flex flex-col gap-4">
          <Alert v-if="preview.selfDeletionForbidden || preview.lastAdministratorProtected" variant="destructive">
            <AlertDescription>
              {{ preview.selfDeletionForbidden ? $t('administration.platformUsers.validation.deleteOwnFormat') : $t('administration.platformUsers.validation.lastAdministratorFormat') }}
            </AlertDescription>
          </Alert>
          <div v-if="preview.references?.length" class="flex flex-col gap-2">
            <p class="text-sm text-muted-foreground">{{ $t('administration.platformUsers.description.userFollowingBusinessReferences') }}</p>
            <div class="flex flex-wrap gap-2">
              <Badge v-for="reference in preview.references" :key="reference.code" variant="secondary">
                {{ REFERENCE_LABELS[reference.code ?? ''] ? translate(REFERENCE_LABELS[reference.code ?? '']!) : reference.code }} × {{ reference.count }}
              </Badge>
            </div>
          </div>
          <p v-else class="text-sm text-muted-foreground">{{ $t('administration.platformUsers.description.userBusinessReferences') }}</p>
          <FieldGroup>
            <Field>
              <FieldLabel for="deletion-mode">{{ $t('administration.label.deleteMethod') }}</FieldLabel>
              <Select v-model="deletionMode">
                <SelectTrigger id="deletion-mode" class="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem v-if="preview.canAnonymize" value="Anonymize">{{ $t('administration.platformUsers.label.anonymizeKeepDataRemove') }}</SelectItem>
                    <SelectItem v-if="preview.canHardDelete" value="HardDelete">{{ $t('administration.label.physicalDeletionCompleteErasure') }}</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="deletion-reason">{{ $t('administration.label.reasonDeletion') }}</FieldLabel>
              <Input id="deletion-reason" v-model="deletionReason" required :placeholder="$t('administration.platformUsers.description.loggedAuditLog')" />
            </Field>
          </FieldGroup>
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            :disabled="deleting || !preview || !deletionReason.trim()
              || preview.selfDeletionForbidden || preview.lastAdministratorProtected"
            @click="confirmDelete"
          >
            <Spinner v-if="deleting" data-icon="inline-start" /> {{ $t('administration.label.confirmDeletion') }} </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
