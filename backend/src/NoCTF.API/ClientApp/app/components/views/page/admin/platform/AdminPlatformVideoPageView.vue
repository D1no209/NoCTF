<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformVideoPageViewState } from '~/features/routes/admin/platform/useAdminPlatformVideoPage'
const props = defineProps<{ state: AdminPlatformVideoPageViewState }>()
const { loading, saving, error, rules, draft, dirty, valid, load, save } = toRefs(props.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <Skeleton v-if="loading" class="h-96 w-full" />
    <Card v-else>
      <CardHeader><CardTitle>{{ $t('liveSolo.videoConfiguration.title') }}</CardTitle><CardDescription>{{ $t('liveSolo.videoConfiguration.ratio') }}</CardDescription></CardHeader>
      <CardContent>
        <UiForm validation="feature" @submit.prevent="save">
          <FieldGroup>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field><FieldLabel for="video-width">{{ $t('liveSolo.videoConfiguration.width') }}</FieldLabel><NullableNumberInput id="video-width" v-model="draft.width" :min="rules?.minimumWidth" :max="rules?.maximumWidth" :step="2" :disabled="saving" /></Field>
              <Field><FieldLabel for="video-height">{{ $t('liveSolo.videoConfiguration.height') }}</FieldLabel><NullableNumberInput id="video-height" v-model="draft.height" :min="rules?.minimumHeight" :max="rules?.maximumHeight" :step="2" :disabled="saving" /></Field>
              <Field><FieldLabel for="video-fps">{{ $t('liveSolo.videoConfiguration.fps') }}</FieldLabel><NullableNumberInput id="video-fps" v-model="draft.fps" :min="rules?.minimumFramesPerSecond" :max="rules?.maximumFramesPerSecond" :disabled="saving" /></Field>
              <Field><FieldLabel for="video-bitrate">{{ $t('liveSolo.videoConfiguration.bitrate') }}</FieldLabel><NullableNumberInput id="video-bitrate" v-model="draft.kilobitsPerSecond" :disabled="saving" /></Field>
            </div>
            <FieldError v-if="dirty && !valid">{{ $t('liveSolo.videoConfiguration.invalid') }}</FieldError>
            <p class="text-sm text-muted-foreground">{{ $t('liveSolo.videoConfiguration.activation') }}</p>
            <p class="text-sm text-muted-foreground">{{ $t('liveSolo.videoConfiguration.programme') }}</p>
            <Field orientation="horizontal"><Button type="submit" :disabled="saving || !dirty || !valid"><Spinner v-if="saving" data-icon="inline-start" />{{ $t('administration.label.saveConfiguration') }}</Button><Button type="button" variant="outline" :disabled="saving" @click="load">{{ $t('common.label.refresh') }}</Button></Field>
          </FieldGroup>
        </UiForm>
      </CardContent>
    </Card>
  </div>
</template>
