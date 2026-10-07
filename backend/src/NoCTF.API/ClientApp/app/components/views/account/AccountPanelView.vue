<script setup lang="ts">
import { toRefs } from 'vue'
import type { AccountPanelViewState } from '~/features/account/useAccountPanel'

const viewProps = defineProps<{ state: AccountPanelViewState }>()
const { UserRound, LockKeyhole, ShieldCheck, ImageIcon, LogOut, user, wideAccountPanel, isImpersonating, fetchMe, open, activeSection, setOpen, selectSection, signOut, description, profilePending, profileError, profileSuccess, profileDirty, saveProfile, avatarInput, avatarPending, avatarEditorOpen, avatarSourceFile, selectAvatar, setAvatarEditorOpen, uploadAvatar, reportAvatarError, setAvatarInputRef, wallpaperInput, wallpaperPending, wallpaperUrl, selectWallpaper, setWallpaperEnabled, setWallpaperInputRef, fullName, studentNumber, identityLoading, identityLoaded, identityPending, identityError, identitySuccess, identityDirty, identityFieldError, loadIdentity, saveIdentity, emailPending, emailMessage, emailError, resendEmail, ssoConfiguration, ssoLoading, ssoLoaded, ssoPending, ssoError, ssoProviderId, loadSsoBinding, beginSsoBinding, unbindSsoIdentity, currentPassword, newPassword, confirmNewPassword, passwordPending, passwordError, changePassword, logoutAll, AvatarCropDialog, AdminDateTime, MfaAccountSecurity, PasskeyAccountSecurity } = toRefs(viewProps.state)
</script>

<template>
  <Popover :open="open" @update:open="setOpen">
    <PopoverTrigger as-child>
      <Button variant="ghost" size="icon" class="rounded-full p-0" :aria-label="$t('account.label.accountSettings')">
        <Avatar class="size-8 after:border-0">
          <AvatarImage v-if="user?.avatarUrl" :src="user.avatarUrl" :alt="user?.userName ?? ''" />
          <AvatarFallback>{{ user?.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
        </Avatar>
      </Button>
    </PopoverTrigger>
    <PopoverContent
      :align="wideAccountPanel ? 'center' : 'end'"
      side="bottom"
      :side-offset="12"
      :collision-padding="wideAccountPanel ? 0 : 12"
      class="account-panel-popover w-[min(20rem,calc(100vw-1rem))] max-w-none p-0"
    >
      <Card size="sm" data-slot="account-panel-menu" class="h-[min(20rem,calc(100vw-1rem))] w-full shrink-0 justify-between gap-3 overflow-hidden py-4">
        <Button v-if="user?.userId" variant="ghost" class="mx-3 h-auto justify-start gap-3 px-2 py-2" as-child>
          <NuxtLink :to="`/users/${user.userId}`" :aria-label="$t('profile.openProfile')" @click="setOpen(false)">
            <Avatar class="size-11 shrink-0 after:border-0">
              <AvatarImage v-if="user.avatarUrl" :src="user.avatarUrl" :alt="user.userName ?? ''" />
              <AvatarFallback>{{ user.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
            </Avatar>
            <span class="min-w-0 flex-1 text-left">
              <span class="block truncate font-semibold">{{ user.userName ?? '-' }}</span>
              <span class="block truncate text-xs font-normal text-muted-foreground">{{ user.email ?? '-' }}</span>
            </span>
          </NuxtLink>
        </Button>

        <div v-else class="flex min-w-0 items-center gap-3 px-4">
          <Avatar class="size-11 shrink-0 after:border-0"><AvatarFallback>?</AvatarFallback></Avatar>
          <span class="truncate font-semibold">-</span>
        </div>

        <div class="grid min-h-0 flex-1 grid-cols-2 grid-rows-2 gap-2 px-4">
          <Button
            type="button"
            variant="ghost"
            data-slot="account-panel-section-button"
            :data-active="activeSection === 'profile' || undefined"
            :aria-pressed="activeSection === 'profile'"
            @click="selectSection('profile')"
          >
            <UserRound />
            <span>{{ $t('account.label.publicProfile') }}</span>
            <span v-if="profileDirty" class="sr-only">{{ $t('account.label.unsavedChanges') }}</span>
          </Button>
          <Button
            type="button"
            variant="ghost"
            data-slot="account-panel-section-button"
            :data-active="activeSection === 'identity' || undefined"
            :aria-pressed="activeSection === 'identity'"
            @click="selectSection('identity')"
          >
            <LockKeyhole />
            <span>{{ $t('accountPanel.accountInformation') }}</span>
            <span v-if="identityDirty" class="sr-only">{{ $t('account.label.unsavedChanges') }}</span>
          </Button>
          <Button
            type="button"
            variant="ghost"
            data-slot="account-panel-section-button"
            :data-active="activeSection === 'wallpaper' || undefined"
            :aria-pressed="activeSection === 'wallpaper'"
            @click="selectSection('wallpaper')"
          >
            <ImageIcon />
            <span>{{ $t('accountPanel.customWallpaper') }}</span>
          </Button>
          <Button
            type="button"
            variant="ghost"
            data-slot="account-panel-section-button"
            :data-active="activeSection === 'security' || undefined"
            :aria-pressed="activeSection === 'security'"
            @click="selectSection('security')"
          >
            <ShieldCheck />
            <span>{{ $t('account.label.accountSecurity') }}</span>
          </Button>
        </div>

        <Button type="button" variant="ghost" size="sm" class="mx-4 justify-start" @click="signOut">
          <LogOut data-icon="inline-start" />{{ isImpersonating ? $t('common.label.exitImpersonation') : $t('account.label.signOut') }}
        </Button>
      </Card>

      <Card v-if="activeSection" size="sm" data-slot="account-panel-detail" class="noctf-motion-detail-enter min-h-0 w-full gap-0 py-0">
        <header class="shrink-0 px-5 py-4">
          <h2 class="font-semibold">
            {{ activeSection === 'profile' ? $t('account.label.publicProfile') : activeSection === 'identity' ? $t('accountPanel.accountInformation') : activeSection === 'wallpaper' ? $t('accountPanel.customWallpaper') : $t('account.label.accountSecurity') }}
          </h2>
        </header>
        <ScrollSurface axis="y" class="min-h-0 flex-1" :aria-label="$t('account.label.accountSettings')">
          <div class="px-5 pb-5">
            <UiForm v-if="activeSection === 'profile'" class="flex flex-col gap-5" @submit.prevent="saveProfile">
              <FieldGroup>
                <Field>
                  <FieldLabel>{{ $t('account.label.avatar') }}</FieldLabel>
                  <div class="flex flex-wrap items-center gap-3">
                    <Avatar class="size-14 shrink-0">
                      <AvatarImage v-if="user?.avatarUrl" :src="user.avatarUrl" :alt="user?.userName ?? ''" />
                      <AvatarFallback>{{ user?.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
                    </Avatar>
                    <FileInput :ref="setAvatarInputRef" accept="image/jpeg,image/png,image/webp" class="sr-only" @change="selectAvatar" />
                    <Button type="button" variant="outline" size="sm" :disabled="avatarPending" @click="avatarInput?.click()">
                      <Spinner v-if="avatarPending" data-icon="inline-start" />{{ $t('account.label.selectCropAvatar') }}
                    </Button>
                  </div>
                </Field>
                <Field>
                  <FieldLabel for="account-panel-description">{{ $t('account.label.profile') }}</FieldLabel>
                  <Textarea id="account-panel-description" v-model="description" :disabled="profilePending" maxlength="500" rows="4" :placeholder="$t('account.label.introduceYourselfOptional')" />
                </Field>
              </FieldGroup>
              <Alert v-if="profileError" variant="destructive"><AlertDescription>{{ $message(profileError) }}</AlertDescription></Alert>
              <div class="flex flex-wrap items-center gap-3">
                <Button type="submit" :disabled="profilePending || !profileDirty"><Spinner v-if="profilePending" data-icon="inline-start" />{{ $t('account.label.savePublicProfile') }}</Button>
                <span role="status" class="text-xs text-muted-foreground">{{ profileDirty ? $t('account.label.unsavedChanges') : profileSuccess ? $t('common.label.saved') : '' }}</span>
              </div>
            </UiForm>

            <section v-else-if="activeSection === 'identity'" class="flex flex-col gap-5">
              <Skeleton v-if="identityLoading" class="h-36 w-full" />
              <Alert v-else-if="!identityLoaded" variant="destructive">
                <AlertDescription>{{ $message(identityError) }}<Button variant="outline" size="sm" @click="loadIdentity">{{ $t('common.label.retry') }}</Button></AlertDescription>
              </Alert>
              <UiForm v-else class="flex flex-col gap-5" @submit.prevent="saveIdentity">
                <FieldGroup>
                  <Field :data-invalid="Boolean(identityFieldError('FullName'))">
                    <FieldLabel for="account-panel-full-name">{{ $t('account.label.fullNameOptional') }}</FieldLabel>
                    <Input id="account-panel-full-name" v-model="fullName" :disabled="identityPending" :aria-invalid="Boolean(identityFieldError('FullName'))" maxlength="100" autocomplete="off" />
                    <FieldError>{{ identityFieldError('FullName') }}</FieldError>
                  </Field>
                  <Field :data-invalid="Boolean(identityFieldError('StudentNumber'))">
                    <FieldLabel for="account-panel-student-number">{{ $t('account.label.studentNumberOptional') }}</FieldLabel>
                    <Input id="account-panel-student-number" v-model="studentNumber" type="text" :disabled="identityPending" :aria-invalid="Boolean(identityFieldError('StudentNumber'))" maxlength="64" autocomplete="off" />
                    <FieldError>{{ identityFieldError('StudentNumber') }}</FieldError>
                  </Field>
                </FieldGroup>
                <Alert v-if="identityError" variant="destructive"><AlertDescription>{{ $message(identityError) }}</AlertDescription></Alert>
                <div class="flex flex-wrap items-center gap-3">
                  <Button type="submit" :disabled="identityPending || !identityDirty"><Spinner v-if="identityPending" data-icon="inline-start" />{{ $t('account.label.savePersonalInformation') }}</Button>
                  <span role="status" class="text-xs text-muted-foreground">{{ identityDirty ? $t('account.label.unsavedChanges') : identitySuccess ? $t('common.label.saved') : '' }}</span>
                </div>
              </UiForm>
            </section>

            <section v-else-if="activeSection === 'wallpaper'" class="flex flex-col gap-5">
              <div class="aspect-video overflow-hidden rounded-xl border bg-muted">
                <img
                  v-if="wallpaperUrl"
                  :src="wallpaperUrl"
                  :alt="$t('accountPanel.wallpaperPreview')"
                  loading="lazy"
                  decoding="async"
                  class="size-full object-cover"
                >
                <div v-else class="flex size-full items-center justify-center px-8 text-center text-sm text-muted-foreground">
                  {{ $t('accountPanel.noWallpaperUploaded') }}
                </div>
              </div>

              <div class="flex items-center justify-between gap-4 rounded-xl border p-4">
                <div class="min-w-0">
                  <Label for="account-panel-wallpaper-enabled" class="font-medium">{{ $t('accountPanel.wallpaperSwitch') }}</Label>
                </div>
                <Switch
                  id="account-panel-wallpaper-enabled"
                  :model-value="Boolean(user?.wallpaperEnabled)"
                  :disabled="wallpaperPending || !user?.wallpaperRevision"
                  @update:model-value="setWallpaperEnabled"
                />
              </div>

              <div class="flex flex-col gap-2">
                <FileInput :ref="setWallpaperInputRef" accept="image/jpeg,image/png,image/webp" class="sr-only" @change="selectWallpaper" />
                <Button type="button" variant="outline" size="sm" :disabled="wallpaperPending" @click="wallpaperInput?.click()">
                  <Spinner v-if="wallpaperPending" data-icon="inline-start" />
                  {{ user?.wallpaperRevision ? $t('accountPanel.replaceWallpaper') : $t('accountPanel.chooseWallpaper') }}
                </Button>
              </div>
            </section>

            <section v-else class="flex flex-col gap-6">
              <component :is="PasskeyAccountSecurity" v-if="!isImpersonating && user?.kind === 'Human'" />
              <component :is="MfaAccountSecurity" v-if="!isImpersonating && user?.kind === 'Human'" />
              <section class="flex flex-col gap-3">
                <h3 class="text-sm font-semibold">{{ $t('common.label.emailVerification') }}</h3>
                <p class="flex flex-wrap items-center gap-2 break-all text-sm">
                  {{ user?.email }}
                  <Badge :variant="user?.emailVerified ? 'secondary' : 'outline'">{{ user?.emailVerified ? $t('common.label.verified.accountPanelView') : $t('common.label.verified') }}</Badge>
                </p>
                <div v-if="!user?.emailVerified" class="flex flex-wrap gap-2">
                  <Button variant="outline" size="sm" :disabled="emailPending" @click="resendEmail"><Spinner v-if="emailPending" data-icon="inline-start" />{{ $t('account.label.sendVerificationEmail') }}</Button>
                  <Button variant="ghost" size="sm" @click="fetchMe">{{ $t('account.label.refreshVerificationStatus') }}</Button>
                </div>
                <Alert v-if="emailMessage" :variant="emailError ? 'destructive' : 'default'"><AlertDescription>{{ $message(emailMessage) }}</AlertDescription></Alert>
              </section>

              <Separator />
              <section v-if="!isImpersonating" class="flex flex-col gap-4">
                <h3 class="text-sm font-semibold">{{ $t('sso.externalIdentity') }}</h3>
                <Skeleton v-if="ssoLoading" class="h-24 w-full" />
                <Alert v-else-if="ssoError" variant="destructive"><AlertDescription>{{ $message(ssoError) }}</AlertDescription></Alert>
                <template v-if="ssoLoaded && ssoConfiguration?.binding">
                  <div class="flex items-center gap-3 rounded-xl border p-4">
                    <img
                      v-if="ssoConfiguration.binding.providerIconUrl"
                      :src="ssoConfiguration.binding.providerIconUrl"
                      class="size-9 shrink-0 rounded-lg object-contain"
                      alt=""
                      aria-hidden="true"
                      decoding="async"
                      referrerpolicy="no-referrer"
                    >
                    <div class="min-w-0">
                      <p class="font-medium">{{ ssoConfiguration.binding.providerName }}</p>
                      <p class="mt-1 break-all text-xs text-muted-foreground">{{ ssoConfiguration.binding.subject }}</p>
                      <div class="mt-2 flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                        <Badge variant="outline">{{ ssoConfiguration.binding.protocol }}</Badge>
                        <span>{{ $t('sso.boundAt') }}</span>
                        <component :is="AdminDateTime" :value="ssoConfiguration.binding.boundAt" />
                      </div>
                    </div>
                  </div>
                  <Button type="button" variant="destructive" size="sm" class="self-start" :disabled="ssoPending" @click="unbindSsoIdentity">
                    <Spinner v-if="ssoPending" data-icon="inline-start" />{{ $t('sso.unbind') }}
                  </Button>
                </template>
                <template v-else-if="ssoLoaded && ssoConfiguration?.providers?.length">
                  <Field>
                    <FieldLabel for="account-panel-sso-provider">{{ $t('sso.identityProvider') }}</FieldLabel>
                    <Select v-model="ssoProviderId" :disabled="ssoPending">
                      <SelectTrigger id="account-panel-sso-provider"><SelectValue /></SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="provider in ssoConfiguration.providers" :key="provider.id" :value="provider.id!">
                          <span class="flex items-center gap-2">
                            <img v-if="provider.iconUrl" :src="provider.iconUrl" class="size-4 object-contain" alt="" aria-hidden="true" decoding="async" referrerpolicy="no-referrer">
                            <span>{{ provider.name }}</span>
                          </span>
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Button type="button" variant="outline" size="sm" class="self-start" :disabled="ssoPending || !ssoProviderId" @click="beginSsoBinding">
                    <Spinner v-if="ssoPending" data-icon="inline-start" />{{ $t('sso.bind') }}
                  </Button>
                </template>
                <p v-else-if="ssoLoaded" class="text-sm text-muted-foreground">{{ $t('sso.noBindableProviders') }}</p>
                <Button v-if="!ssoLoaded && !ssoLoading" type="button" variant="outline" size="sm" class="self-start" @click="loadSsoBinding">{{ $t('common.label.retry') }}</Button>
              </section>

              <Separator v-if="!isImpersonating" />
              <UiForm class="flex flex-col gap-4" @submit.prevent="changePassword">
                <h3 class="text-sm font-semibold">{{ $t('account.label.changePassword') }}</h3>
                <FieldGroup>
                  <Field><FieldLabel for="account-panel-current-password">{{ $t('account.label.password') }}</FieldLabel><PasswordInput id="account-panel-current-password" v-model="currentPassword" :disabled="passwordPending" autocomplete="current-password" required /></Field>
                  <Field><FieldLabel for="account-panel-new-password">{{ $t('common.label.newPassword') }}</FieldLabel><PasswordInput id="account-panel-new-password" v-model="newPassword" :disabled="passwordPending" autocomplete="new-password" required minlength="8" maxlength="1024" /></Field>
                  <Field :data-invalid="Boolean(passwordError)"><FieldLabel for="account-panel-confirm-password">{{ $t('common.label.confirmNewPassword') }}</FieldLabel><PasswordInput id="account-panel-confirm-password" v-model="confirmNewPassword" :disabled="passwordPending" :aria-invalid="Boolean(passwordError)" autocomplete="new-password" required /><FieldError v-if="passwordError">{{ $message(passwordError) }}</FieldError></Field>
                </FieldGroup>
                <Button type="submit" class="self-start" :disabled="passwordPending"><Spinner v-if="passwordPending" data-icon="inline-start" />{{ $t('account.label.changePassword') }}</Button>
              </UiForm>

              <AlertDialog v-if="!isImpersonating">
                <AlertDialogTrigger as-child><Button variant="outline" size="sm" class="self-start">{{ $t('account.accountPanel.label.logOutSessions') }}</Button></AlertDialogTrigger>
                <AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t('account.accountPanel.description.sureLogOutSessions') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('account.accountPanel.description.signStatusInvalidatedImmediately') }}</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel><AlertDialogAction @click="logoutAll">{{ $t('account.label.confirmLogout') }}</AlertDialogAction></AlertDialogFooter></AlertDialogContent>
              </AlertDialog>
            </section>
          </div>
        </ScrollSurface>
      </Card>
    </PopoverContent>
  </Popover>

  <component :is="AvatarCropDialog" :open="avatarEditorOpen" :file="avatarSourceFile" :saving="avatarPending" @update:open="setAvatarEditorOpen" @save="uploadAvatar" @error="reportAvatarError" />
</template>
