<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformExperimentsPageViewState } from '~/features/routes/admin/platform/useAdminPlatformExperimentsPage'

const viewProps = defineProps<{ state: AdminPlatformExperimentsPageViewState }>()
const { Beaker, RefreshCw, loading, saving, loadError, ctfPatchVerificationEnabled, dirty, load, save } = toRefs(viewProps.state)
</script>

<template>
  <section class="flex min-w-0 flex-col gap-6">
    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ $message(loadError) }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading" class="h-72 w-full" />

    <Card v-else>
      <CardHeader>
        <CardTitle class="flex items-center gap-2">
          <Beaker class="size-5" />
          {{ $t('ui.experimentalFeatures') }}
        </CardTitle>
        <CardDescription>{{ $t('ui.experimentalFeaturesDescription') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <UiForm class="flex flex-col gap-6" @submit.prevent="save">
          <FieldGroup>
            <Field orientation="horizontal">
              <Switch
                id="ctf-patch-verification-enabled"
                v-model="ctfPatchVerificationEnabled"
                :disabled="saving"
              />
              <FieldContent>
                <FieldLabel for="ctf-patch-verification-enabled">
                  {{ $t('ui.ctfPatchVerification') }}
                </FieldLabel>
                <FieldDescription>{{ $t('ui.ctfPatchVerificationDescription') }}</FieldDescription>
              </FieldContent>
              <Badge variant="secondary">{{ $t('ui.experimental') }}</Badge>
            </Field>
          </FieldGroup>
          <div class="flex flex-wrap gap-3">
            <Button type="submit" :disabled="saving || !dirty">
              <Spinner v-if="saving" data-icon="inline-start" />
              {{ $t('ui.saveChanges') }}
            </Button>
            <Button type="button" variant="outline" :disabled="saving" @click="load">
              <RefreshCw data-icon="inline-start" />
              {{ $t('ui.reload') }}
            </Button>
          </div>
        </UiForm>
      </CardContent>
    </Card>
  </section>
</template>
