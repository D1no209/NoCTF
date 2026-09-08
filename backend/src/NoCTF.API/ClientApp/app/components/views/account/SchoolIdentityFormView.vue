<script setup lang="ts">
import { toRefs } from 'vue'
import type { SchoolIdentityFormViewState } from '~/features/account/useSchoolIdentityForm'

const viewProps = defineProps<{ state: SchoolIdentityFormViewState }>()
const { LockKeyhole, fullName, studentNumber, saved, loading, loaded, pending, retentionDays, error, success, fieldError, dirty, load, save } = toRefs(viewProps.state)
</script>

<template>
  <section class="flex flex-col gap-6">
    <header class="flex flex-col gap-2">
      <h2 class="flex items-center gap-2 text-xl font-semibold"><LockKeyhole class="size-5" />{{ $t('ui.personalInformation') }}</h2>
      <p class="max-w-prose text-sm text-muted-foreground">{{ $t('ui.usedOnlyForCompetitionIdentityChecksNotPublicVisibleOnly') }}</p>
      <p class="text-sm text-muted-foreground">{{ $t('ui.selfReportedInformationNotVerifiedIdentityLeavingItBlankOr') }}</p>
    </header>
    <Skeleton v-if="loading" class="h-40 w-full" />
    <Alert v-else-if="!loaded" variant="destructive"><AlertDescription>{{ $message(error) }}<Button variant="outline" @click="load">{{ $t('ui.retry') }}</Button></AlertDescription></Alert>
    <form v-else class="flex flex-col gap-6" @submit.prevent="save">
      <FieldGroup>
        <Field :data-invalid="Boolean(fieldError('FullName'))">
          <FieldLabel for="school-name">{{ $t('ui.fullNameOptional') }}</FieldLabel>
          <Input id="school-name" v-model="fullName" :disabled="pending" :aria-invalid="Boolean(fieldError('FullName'))" aria-describedby="school-name-error" maxlength="100" autocomplete="off" />
          <FieldError id="school-name-error">{{ fieldError('FullName') }}</FieldError>
        </Field>
        <Field :data-invalid="Boolean(fieldError('StudentNumber'))">
          <FieldLabel for="student-number">{{ $t('ui.studentNumberOptional') }}</FieldLabel>
          <Input id="student-number" v-model="studentNumber" :disabled="pending" :aria-invalid="Boolean(fieldError('StudentNumber'))" aria-describedby="student-number-error" type="text" maxlength="64" autocomplete="off" />
          <FieldError id="student-number-error">{{ fieldError('StudentNumber') }}</FieldError>
          <FieldDescription>{{ $t('ui.lettersAndNumbersAreSupportedLeadingZerosArePreservedSave') }}</FieldDescription>
        </Field>
      </FieldGroup>
      <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
      <div class="flex flex-wrap items-center gap-3">
        <Button type="submit" :disabled="pending || !dirty"><Spinner v-if="pending" data-icon="inline-start" />{{ $t('ui.savePersonalInformation') }}</Button>
        <Button v-if="error && !dirty" type="button" variant="outline" @click="load">{{ $t('ui.retry') }}</Button>
        <span role="status" class="text-sm text-muted-foreground">{{ dirty ? $t('ui.unsavedChanges') : success ? $t('ui.saved') : '' }}</span>
      </div>
    </form>
    <Separator />
    <p v-if="retentionDays" class="text-xs text-muted-foreground">{{ $t('ui.forCompetitionChecksAndSecurityInvestigationsSourceIpsAreRetained', { days: retentionDays }) }}</p>
  </section>
</template>
