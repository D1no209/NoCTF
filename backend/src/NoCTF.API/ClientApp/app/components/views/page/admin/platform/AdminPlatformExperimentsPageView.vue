<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformExperimentsPageViewState } from '~/features/routes/admin/platform/useAdminPlatformExperimentsPage'

const viewProps = defineProps<{ state: AdminPlatformExperimentsPageViewState }>()
const { Beaker, RefreshCw, loading, saving, loadError, ctfPatchVerificationEnabled, dirty, load, save,
  videoWidth,videoHeight,videoFps,videoKbps,videoRules,videoDirty,videoValid,saveVideo } = toRefs(viewProps.state)
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
          <component :is="Beaker" class="size-5" />
          {{ $t('administration.label.experimentalFeatures') }}
        </CardTitle>
        <CardDescription>{{ $t('administration.label.experimentalFeaturesDescription') }}</CardDescription>
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
                  {{ $t('administration.label.ctfPatchVerification') }}
                </FieldLabel>
                <FieldDescription>{{ $t('administration.label.ctfPatchVerificationDescription') }}</FieldDescription>
              </FieldContent>
              <Badge variant="secondary">{{ $t('administration.label.experimental') }}</Badge>
            </Field>
          </FieldGroup>
          <div class="flex flex-wrap gap-3">
            <Button type="submit" :disabled="saving || !dirty">
              <Spinner v-if="saving" data-icon="inline-start" />
              {{ $t('administration.label.saveChanges') }}
            </Button>
            <Button type="button" variant="outline" :disabled="saving || dirty || videoDirty" @click="load">
              <component :is="RefreshCw" data-icon="inline-start" />
              {{ $t('common.label.reload') }}
            </Button>
          </div>
        </UiForm>
      </CardContent>
    </Card>
    <Card v-if="!loading&&videoRules"><CardHeader><CardTitle>{{ $t('liveSolo.videoConfiguration.title') }}</CardTitle></CardHeader><CardContent>
      <UiForm validation="feature" class="flex flex-col gap-5" @submit="saveVideo">
        <FieldGroup class="grid gap-4 md:grid-cols-2">
          <Field><FieldLabel for="video-width">{{ $t('liveSolo.videoConfiguration.width') }}</FieldLabel><NumberInput id="video-width" v-model="videoWidth" :min="videoRules.minimumWidth" :max="videoRules.maximumWidth" :step="2" :disabled="saving" /></Field>
          <Field><FieldLabel for="video-height">{{ $t('liveSolo.videoConfiguration.height') }}</FieldLabel><NumberInput id="video-height" v-model="videoHeight" :min="videoRules.minimumHeight" :max="videoRules.maximumHeight" :step="2" :disabled="saving" /></Field>
          <Field><FieldLabel for="video-fps">{{ $t('liveSolo.videoConfiguration.fps') }}</FieldLabel><NumberInput id="video-fps" v-model="videoFps" :min="videoRules.minimumFramesPerSecond" :max="videoRules.maximumFramesPerSecond" :step="1" :disabled="saving" /></Field>
          <Field><FieldLabel for="video-kbps">{{ $t('liveSolo.videoConfiguration.bitrate') }}</FieldLabel><NumberInput id="video-kbps" v-model="videoKbps" :min="(videoRules.minimumBitrateBitsPerSecond ?? 0) / 1000" :max="(videoRules.maximumBitrateBitsPerSecond ?? 0) / 1000" :step="1" :disabled="saving" /></Field>
        </FieldGroup>
        <FieldDescription>{{ $t('liveSolo.videoConfiguration.ratio') }}</FieldDescription>
        <FieldDescription>{{ $t('liveSolo.videoConfiguration.activation') }}</FieldDescription>
        <FieldDescription>{{ $t('liveSolo.videoConfiguration.programme') }}</FieldDescription>
        <p v-if="videoDirty&&!videoValid" class="text-sm text-destructive">{{ $t('liveSolo.videoConfiguration.invalid') }}</p>
        <div><Button type="submit" :disabled="saving||!videoDirty||!videoValid"><Spinner v-if="saving" />{{ $t('administration.label.saveChanges') }}</Button></div>
      </UiForm>
    </CardContent></Card>
  </section>
</template>
