<script setup lang="ts">
import { toRefs } from 'vue'
import type { AccountIndexPageViewState } from '~/features/routes/account/useAccountIndexPage'

const viewProps = defineProps<{ state: AccountIndexPageViewState }>()
const { UserRound, LockKeyhole, ShieldCheck, toast, user, fetchMe, logoutAll, description, profilePending, profileError, profileSuccess, profileDirty, schoolDirty, passwordError, emailPending, emailMessage, emailError, resendEmail, saveProfile, avatarInput, avatarPending, avatarEditorOpen, avatarSourceFile, selectAvatar, setAvatarEditorOpen, uploadAvatar, currentPassword, newPassword, confirmNewPassword, passwordPending, changePassword, AvatarCropDialog, SchoolIdentityForm, setAvatarInputRef, onDirtySchoolDirty } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex max-w-5xl flex-col gap-8 px-4 py-8 md:py-12">
    <header class="flex flex-col gap-2">
      <h1 class="text-display text-2xl">{{ $t('ui.accountSettings') }}</h1>
      <p class="text-sm text-muted-foreground">{{ $t('ui.manageYourPublicProfilePrivateInformationAndAccountSecuritySeparately') }}</p>
    </header>
    <Separator />
    <Tabs default-value="profile" orientation="vertical" class="flex-col gap-8 md:flex-row md:gap-12">
      <TabsList variant="line" class="w-full shrink-0 items-stretch md:w-48">
        <TabsTrigger value="profile" class="justify-start gap-2"><UserRound />{{ $t('ui.publicProfile') }}<span v-if="profileDirty" :aria-label="$t('ui.unsavedChanges')">•</span></TabsTrigger>
        <TabsTrigger value="school" class="justify-start gap-2"><LockKeyhole />{{ $t('ui.personalInformation') }}<span v-if="schoolDirty" :aria-label="$t('ui.unsavedChanges')">•</span></TabsTrigger>
        <TabsTrigger value="security" class="justify-start gap-2"><ShieldCheck />{{ $t('ui.accountSecurity') }}</TabsTrigger>
      </TabsList>
      <div class="min-w-0 flex-1">
        <TabsContent value="profile" class="m-0">
          <form class="flex flex-col gap-6" @submit.prevent="saveProfile">
            <header class="flex flex-col gap-2"><h2 class="text-xl font-semibold">{{ $t('ui.publicProfile') }}</h2><p class="text-sm text-muted-foreground">{{ $t('ui.yourAvatarUsernameAndBioAreVisibleOnYourPublic') }}</p></header>
            <FieldGroup>
              <Field>
                <FieldLabel>{{ $t('ui.avatar') }}</FieldLabel>
                <div class="flex flex-wrap items-center gap-4">
                  <Avatar class="size-16"><AvatarImage v-if="user?.avatarUrl" :src="user.avatarUrl" :alt="user?.userName ?? ''" /><AvatarFallback>{{ user?.userName?.slice(0, 2) ?? '?' }}</AvatarFallback></Avatar>
                  <div class="flex flex-col items-start gap-2">
                    <FileInput :ref="setAvatarInputRef" type="file" accept="image/jpeg,image/png,image/webp" class="sr-only" @change="selectAvatar" />
                    <Button type="button" variant="outline" :disabled="avatarPending" @click="avatarInput?.click()"><Spinner v-if="avatarPending" data-icon="inline-start" />{{ $t('ui.selectAndCropAvatar') }}</Button>
                    <span class="text-xs text-muted-foreground">{{ $t('ui.confirmingTheCropSavesYourAvatarSeparatelyWithoutChangingOther') }}</span>
                  </div>
                </div>
              </Field>
              <Field><FieldLabel for="account-username">{{ $t('ui.username') }}</FieldLabel><Input id="account-username" :model-value="user?.userName ?? ''" readonly /><FieldDescription>{{ $t('ui.yourUsernameIsAPublicAccountIdentifierAndCannotBe') }}</FieldDescription></Field>
              <Field><FieldLabel for="description">{{ $t('ui.profile') }}</FieldLabel><Textarea id="description" v-model="description" :disabled="profilePending" maxlength="500" rows="5" :placeholder="$t('ui.introduceYourselfOptional')" /><FieldDescription>{{ description.length }} {{ $t('ui.500') }}</FieldDescription></Field>
            </FieldGroup>
            <Alert v-if="profileError" variant="destructive"><AlertDescription>{{ $message(profileError) }}</AlertDescription></Alert>
            <div class="flex flex-wrap items-center gap-3">
              <Button type="submit" :disabled="profilePending || !profileDirty"><Spinner v-if="profilePending" data-icon="inline-start" />{{ $t('ui.savePublicProfile') }}</Button>
              <span role="status" class="text-sm text-muted-foreground">{{ profileDirty ? $t('ui.unsavedChanges') : profileSuccess ? $t('ui.saved') : '' }}</span>
            </div>
          </form>
        </TabsContent>
        <TabsContent value="school" force-mount class="m-0 data-[state=inactive]:hidden"><component :is="SchoolIdentityForm" @dirty="onDirtySchoolDirty" /></TabsContent>
        <TabsContent value="security" class="m-0">
          <section class="flex flex-col gap-8">
            <header><h2 class="text-xl font-semibold">{{ $t('ui.accountSecurity') }}</h2></header>
            <section class="flex flex-col gap-4">
              <h3 class="font-semibold">{{ $t('ui.emailVerification') }}</h3>
              <p class="flex flex-wrap items-center gap-2 break-all">{{ user?.email }}<Badge :variant="user?.emailVerified ? 'secondary' : 'outline'">{{ user?.emailVerified ? $t('ui.verified') : $t('ui.notVerified') }}</Badge></p>
              <div v-if="!user?.emailVerified" class="flex flex-wrap gap-2"><Button variant="outline" :disabled="emailPending" @click="resendEmail"><Spinner v-if="emailPending" data-icon="inline-start" />{{ $t('ui.sendVerificationEmail') }}</Button><Button variant="ghost" @click="fetchMe">{{ $t('ui.refreshVerificationStatus') }}</Button></div>
              <Alert v-if="emailMessage" :variant="emailError ? 'destructive' : 'default'"><AlertDescription>{{ emailMessage }}</AlertDescription></Alert>
            </section>
            <Separator />
            <form class="flex flex-col gap-5" @submit.prevent="changePassword">
              <div><h3 class="font-semibold">{{ $t('ui.changePassword') }}</h3><p class="mt-1 text-sm text-muted-foreground">{{ $t('ui.afterSuccessfulModificationAllDevicesNeedToLogInAgain') }}</p></div>
              <FieldGroup>
                <Field><FieldLabel for="currentPassword">{{ $t('ui.currentPassword') }}</FieldLabel><PasswordInput id="currentPassword" v-model="currentPassword" :disabled="passwordPending" autocomplete="current-password" required /></Field>
                <Field><FieldLabel for="newPassword">{{ $t('ui.newPassword') }}</FieldLabel><PasswordInput id="newPassword" v-model="newPassword" :disabled="passwordPending" autocomplete="new-password" required minlength="8" maxlength="1024" /></Field>
                <Field :data-invalid="Boolean(passwordError)"><FieldLabel for="confirmNewPassword">{{ $t('ui.confirmNewPassword') }}</FieldLabel><PasswordInput id="confirmNewPassword" v-model="confirmNewPassword" :disabled="passwordPending" :aria-invalid="Boolean(passwordError)" autocomplete="new-password" required /><FieldError v-if="passwordError">{{ $message(passwordError) }}</FieldError></Field>
              </FieldGroup>
              <div class="flex flex-wrap items-center gap-3"><Button type="submit" :disabled="passwordPending"><Spinner v-if="passwordPending" data-icon="inline-start" />{{ $t('ui.changePassword') }}</Button><span v-if="currentPassword || newPassword || confirmNewPassword" class="text-sm text-muted-foreground">{{ $t('ui.passwordChangeNotSubmitted') }}</span></div>
            </form>
            <Separator />
            <AlertDialog>
              <AlertDialogTrigger as-child><Button variant="outline" class="self-start">{{ $t('ui.logOutOfAllSessions') }}</Button></AlertDialogTrigger>
              <AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t('ui.areYouSureToLogOutOfAllSessions') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('ui.signInStatusWillBeInvalidatedImmediatelyOnAllDevices') }}</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel>{{ $t('ui.cancel') }}</AlertDialogCancel><AlertDialogAction @click="logoutAll">{{ $t('ui.confirmLogout') }}</AlertDialogAction></AlertDialogFooter></AlertDialogContent>
            </AlertDialog>
          </section>
        </TabsContent>
      </div>
    </Tabs>
    <component :is="AvatarCropDialog" :open="avatarEditorOpen" :file="avatarSourceFile" :saving="avatarPending" @update:open="setAvatarEditorOpen" @save="uploadAvatar" @error="toast.error($event.message)" />
  </div>
</template>
