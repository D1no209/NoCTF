<script setup lang="ts">
import { toRefs } from 'vue'
import type { AccountPanelViewState } from '~/features/account/useAccountPanel'

const viewProps = defineProps<{ state: AccountPanelViewState }>()
const { UserRound, LockKeyhole, ShieldCheck, ImageIcon, LogOut, user, isImpersonating, fetchMe, open, activeSection, setOpen, selectSection, signOut, description, profilePending, profileError, profileSuccess, profileDirty, saveProfile, avatarInput, avatarPending, avatarEditorOpen, avatarSourceFile, avatarRequirements, selectAvatar, setAvatarEditorOpen, uploadAvatar, reportAvatarError, setAvatarInputRef, wallpaperInput, wallpaperPending, wallpaperUrl, wallpaperRequirements, selectWallpaper, setWallpaperEnabled, setWallpaperInputRef, fullName, studentNumber, identityLoading, identityLoaded, identityPending, identityError, identitySuccess, identityDirty, identityFieldError, loadIdentity, saveIdentity, emailPending, emailMessage, emailError, resendEmail, ssoConfiguration, ssoLoading, ssoLoaded, ssoPending, ssoError, ssoProviderId, ssoPassword, loadSsoBinding, beginSsoBinding, unbindSsoIdentity, currentPassword, newPassword, confirmNewPassword, passwordPending, passwordError, changePassword, logoutAll, AvatarCropDialog } = toRefs(viewProps.state)
</script>

<template>
  <Popover :open="open" @update:open="setOpen">
    <PopoverTrigger as-child>
      <Button variant="ghost" size="icon" class="rounded-full p-0" :aria-label="$t('ui.accountSettings')">
        <Avatar class="size-8 after:border-0">
          <AvatarImage v-if="user?.avatarUrl" :src="user.avatarUrl" :alt="user?.userName ?? ''" />
          <AvatarFallback>{{ user?.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
        </Avatar>
      </Button>
    </PopoverTrigger>
    <PopoverContent align="end" side="bottom" :side-offset="12" class="account-panel-popover w-[min(26rem,calc(100vw-2rem))] max-w-none p-0">
      <Card size="sm" data-slot="account-panel-menu" class="shrink-0 gap-3 py-4">
        <div class="flex min-w-0 items-center gap-3 px-4">
          <Avatar class="size-11 shrink-0 after:border-0">
            <AvatarImage v-if="user?.avatarUrl" :src="user.avatarUrl" :alt="user?.userName ?? ''" />
            <AvatarFallback>{{ user?.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
          </Avatar>
          <div class="min-w-0 flex-1">
            <p class="truncate font-semibold">{{ user?.userName ?? '-' }}</p>
            <p class="truncate text-xs text-muted-foreground">{{ user?.email ?? '-' }}</p>
          </div>
        </div>

        <div class="grid grid-cols-4 gap-1.5 px-4">
          <Button
            type="button"
            variant="ghost"
            data-slot="account-panel-section-button"
            :data-active="activeSection === 'profile' || undefined"
            :aria-pressed="activeSection === 'profile'"
            @click="selectSection('profile')"
          >
            <UserRound />
            <span>{{ $t('ui.publicProfile') }}</span>
            <span v-if="profileDirty" class="sr-only">{{ $t('ui.unsavedChanges') }}</span>
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
            <span v-if="identityDirty" class="sr-only">{{ $t('ui.unsavedChanges') }}</span>
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
            <span>{{ $t('ui.accountSecurity') }}</span>
          </Button>
        </div>

        <Button type="button" variant="ghost" size="sm" class="mx-4 justify-start" @click="signOut">
          <LogOut data-icon="inline-start" />{{ isImpersonating ? $t('ui.exitImpersonation') : $t('ui.signOut') }}
        </Button>
      </Card>

      <Card v-if="activeSection" size="sm" data-slot="account-panel-detail" class="noctf-motion-detail-enter min-h-0 gap-0 py-0">
        <header class="shrink-0 px-5 py-4">
          <h2 class="font-semibold">
            {{ activeSection === 'profile' ? $t('ui.publicProfile') : activeSection === 'identity' ? $t('accountPanel.accountInformation') : activeSection === 'wallpaper' ? $t('accountPanel.customWallpaper') : $t('ui.accountSecurity') }}
          </h2>
        </header>
        <ScrollSurface axis="y" class="min-h-0 flex-1" :aria-label="$t('ui.accountSettings')">
          <div class="px-5 pb-5">
            <UiForm v-if="activeSection === 'profile'" class="flex flex-col gap-5" @submit.prevent="saveProfile">
              <FieldGroup>
                <Field>
                  <FieldLabel>{{ $t('ui.avatar') }}</FieldLabel>
                  <div class="flex flex-wrap items-center gap-3">
                    <Avatar class="size-14 shrink-0">
                      <AvatarImage v-if="user?.avatarUrl" :src="user.avatarUrl" :alt="user?.userName ?? ''" />
                      <AvatarFallback>{{ user?.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
                    </Avatar>
                    <FileInput :ref="setAvatarInputRef" accept="image/jpeg,image/png,image/webp" class="sr-only" @change="selectAvatar" />
                    <Button type="button" variant="outline" size="sm" :disabled="avatarPending" @click="avatarInput?.click()">
                      <Spinner v-if="avatarPending" data-icon="inline-start" />{{ $t('ui.selectAndCropAvatar') }}
                    </Button>
                  </div>
                  <FieldDescription>{{ avatarRequirements }}</FieldDescription>
                </Field>
                <Field>
                  <FieldLabel for="account-panel-description">{{ $t('ui.profile') }}</FieldLabel>
                  <Textarea id="account-panel-description" v-model="description" :disabled="profilePending" maxlength="500" rows="4" :placeholder="$t('ui.introduceYourselfOptional')" />
                  <FieldDescription>{{ description.length }} {{ $t('ui.500') }}</FieldDescription>
                </Field>
              </FieldGroup>
              <Alert v-if="profileError" variant="destructive"><AlertDescription>{{ $message(profileError) }}</AlertDescription></Alert>
              <div class="flex flex-wrap items-center gap-3">
                <Button type="submit" :disabled="profilePending || !profileDirty"><Spinner v-if="profilePending" data-icon="inline-start" />{{ $t('ui.savePublicProfile') }}</Button>
                <span role="status" class="text-xs text-muted-foreground">{{ profileDirty ? $t('ui.unsavedChanges') : profileSuccess ? $t('ui.saved') : '' }}</span>
              </div>
            </UiForm>

            <section v-else-if="activeSection === 'identity'" class="flex flex-col gap-5">
              <Skeleton v-if="identityLoading" class="h-36 w-full" />
              <Alert v-else-if="!identityLoaded" variant="destructive">
                <AlertDescription>{{ $message(identityError) }}<Button variant="outline" size="sm" @click="loadIdentity">{{ $t('ui.retry') }}</Button></AlertDescription>
              </Alert>
              <UiForm v-else class="flex flex-col gap-5" @submit.prevent="saveIdentity">
                <FieldGroup>
                  <Field :data-invalid="Boolean(identityFieldError('FullName'))">
                    <FieldLabel for="account-panel-full-name">{{ $t('ui.fullNameOptional') }}</FieldLabel>
                    <Input id="account-panel-full-name" v-model="fullName" :disabled="identityPending" :aria-invalid="Boolean(identityFieldError('FullName'))" maxlength="100" autocomplete="off" />
                    <FieldError>{{ identityFieldError('FullName') }}</FieldError>
                  </Field>
                  <Field :data-invalid="Boolean(identityFieldError('StudentNumber'))">
                    <FieldLabel for="account-panel-student-number">{{ $t('ui.studentNumberOptional') }}</FieldLabel>
                    <Input id="account-panel-student-number" v-model="studentNumber" type="text" :disabled="identityPending" :aria-invalid="Boolean(identityFieldError('StudentNumber'))" maxlength="64" autocomplete="off" />
                    <FieldError>{{ identityFieldError('StudentNumber') }}</FieldError>
                  </Field>
                </FieldGroup>
                <Alert v-if="identityError" variant="destructive"><AlertDescription>{{ $message(identityError) }}</AlertDescription></Alert>
                <div class="flex flex-wrap items-center gap-3">
                  <Button type="submit" :disabled="identityPending || !identityDirty"><Spinner v-if="identityPending" data-icon="inline-start" />{{ $t('ui.savePersonalInformation') }}</Button>
                  <span role="status" class="text-xs text-muted-foreground">{{ identityDirty ? $t('ui.unsavedChanges') : identitySuccess ? $t('ui.saved') : '' }}</span>
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
                  <p class="mt-1 text-xs leading-relaxed text-muted-foreground">
                    {{ user?.wallpaperRevision ? $t('accountPanel.wallpaperSwitchDescription') : $t('accountPanel.uploadWallpaperFirst') }}
                  </p>
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
                <p class="text-xs leading-relaxed text-muted-foreground">{{ wallpaperRequirements }}</p>
              </div>
            </section>

            <section v-else class="flex flex-col gap-6">
              <section class="flex flex-col gap-3">
                <h3 class="text-sm font-semibold">{{ $t('ui.emailVerification') }}</h3>
                <p class="flex flex-wrap items-center gap-2 break-all text-sm">
                  {{ user?.email }}
                  <Badge :variant="user?.emailVerified ? 'secondary' : 'outline'">{{ user?.emailVerified ? $t('ui.verified') : $t('ui.notVerified') }}</Badge>
                </p>
                <div v-if="!user?.emailVerified" class="flex flex-wrap gap-2">
                  <Button variant="outline" size="sm" :disabled="emailPending" @click="resendEmail"><Spinner v-if="emailPending" data-icon="inline-start" />{{ $t('ui.sendVerificationEmail') }}</Button>
                  <Button variant="ghost" size="sm" @click="fetchMe">{{ $t('ui.refreshVerificationStatus') }}</Button>
                </div>
                <Alert v-if="emailMessage" :variant="emailError ? 'destructive' : 'default'"><AlertDescription>{{ $message(emailMessage) }}</AlertDescription></Alert>
              </section>

              <Separator />
              <section v-if="!isImpersonating" class="flex flex-col gap-4">
                <h3 class="text-sm font-semibold">{{ $t('sso.externalIdentity') }}</h3>
                <Skeleton v-if="ssoLoading" class="h-24 w-full" />
                <Alert v-else-if="ssoError" variant="destructive"><AlertDescription>{{ $message(ssoError) }}</AlertDescription></Alert>
                <template v-if="ssoLoaded && ssoConfiguration?.binding">
                  <div class="rounded-xl border p-4">
                    <p class="font-medium">{{ ssoConfiguration.binding.providerName }}</p>
                    <p class="mt-1 break-all text-xs text-muted-foreground">{{ ssoConfiguration.binding.subject }}</p>
                  </div>
                  <Field>
                    <FieldLabel for="account-panel-sso-unbind-password">{{ $t('ui.currentPassword') }}</FieldLabel>
                    <PasswordInput id="account-panel-sso-unbind-password" v-model="ssoPassword" :disabled="ssoPending" autocomplete="current-password" />
                  </Field>
                  <Button type="button" variant="destructive" size="sm" class="self-start" :disabled="ssoPending || !ssoPassword" @click="unbindSsoIdentity">
                    <Spinner v-if="ssoPending" data-icon="inline-start" />{{ $t('sso.unbind') }}
                  </Button>
                </template>
                <template v-else-if="ssoLoaded && ssoConfiguration?.providers?.length">
                  <Field>
                    <FieldLabel for="account-panel-sso-provider">{{ $t('sso.identityProvider') }}</FieldLabel>
                    <Select v-model="ssoProviderId" :disabled="ssoPending">
                      <SelectTrigger id="account-panel-sso-provider"><SelectValue /></SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="provider in ssoConfiguration.providers" :key="provider.id" :value="provider.id!">{{ provider.name }}</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel for="account-panel-sso-bind-password">{{ $t('ui.currentPassword') }}</FieldLabel>
                    <PasswordInput id="account-panel-sso-bind-password" v-model="ssoPassword" :disabled="ssoPending" autocomplete="current-password" />
                  </Field>
                  <Button type="button" variant="outline" size="sm" class="self-start" :disabled="ssoPending || !ssoPassword || !ssoProviderId" @click="beginSsoBinding">
                    <Spinner v-if="ssoPending" data-icon="inline-start" />{{ $t('sso.bind') }}
                  </Button>
                </template>
                <p v-else-if="ssoLoaded" class="text-sm text-muted-foreground">{{ $t('sso.noBindableProviders') }}</p>
                <Button v-if="!ssoLoaded && !ssoLoading" type="button" variant="outline" size="sm" class="self-start" @click="loadSsoBinding">{{ $t('ui.retry') }}</Button>
              </section>

              <Separator v-if="!isImpersonating" />
              <UiForm class="flex flex-col gap-4" @submit.prevent="changePassword">
                <h3 class="text-sm font-semibold">{{ $t('ui.changePassword') }}</h3>
                <FieldGroup>
                  <Field><FieldLabel for="account-panel-current-password">{{ $t('ui.currentPassword') }}</FieldLabel><PasswordInput id="account-panel-current-password" v-model="currentPassword" :disabled="passwordPending" autocomplete="current-password" required /></Field>
                  <Field><FieldLabel for="account-panel-new-password">{{ $t('ui.newPassword') }}</FieldLabel><PasswordInput id="account-panel-new-password" v-model="newPassword" :disabled="passwordPending" autocomplete="new-password" required minlength="8" maxlength="1024" /></Field>
                  <Field :data-invalid="Boolean(passwordError)"><FieldLabel for="account-panel-confirm-password">{{ $t('ui.confirmNewPassword') }}</FieldLabel><PasswordInput id="account-panel-confirm-password" v-model="confirmNewPassword" :disabled="passwordPending" :aria-invalid="Boolean(passwordError)" autocomplete="new-password" required /><FieldError v-if="passwordError">{{ $message(passwordError) }}</FieldError></Field>
                </FieldGroup>
                <Button type="submit" class="self-start" :disabled="passwordPending"><Spinner v-if="passwordPending" data-icon="inline-start" />{{ $t('ui.changePassword') }}</Button>
              </UiForm>

              <AlertDialog v-if="!isImpersonating">
                <AlertDialogTrigger as-child><Button variant="outline" size="sm" class="self-start">{{ $t('ui.logOutOfAllSessions') }}</Button></AlertDialogTrigger>
                <AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t('ui.areYouSureToLogOutOfAllSessions') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('ui.signInStatusWillBeInvalidatedImmediatelyOnAllDevices') }}</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel><AlertDialogAction @click="logoutAll">{{ $t('ui.confirmLogout') }}</AlertDialogAction></AlertDialogFooter></AlertDialogContent>
              </AlertDialog>
            </section>
          </div>
        </ScrollSurface>
      </Card>
    </PopoverContent>
  </Popover>

  <component :is="AvatarCropDialog" :open="avatarEditorOpen" :file="avatarSourceFile" :saving="avatarPending" @update:open="setAvatarEditorOpen" @save="uploadAvatar" @error="reportAvatarError" />
</template>
