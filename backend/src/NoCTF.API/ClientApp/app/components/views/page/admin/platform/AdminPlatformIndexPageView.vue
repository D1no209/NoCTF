<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformIndexPageViewState } from '~/features/routes/admin/platform/useAdminPlatformIndexPage'

const viewProps = defineProps<{ state: AdminPlatformIndexPageViewState }>()
const { Upload, information, loading, loadError, name, description, saving, logoInput, logoUploading, logoSrc, save, uploadLogo, setLogoInputRef } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ $message(loadError) }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading" class="h-[34rem] w-full" />

    <Card v-else>
      <CardHeader>
        <CardTitle>{{ $t('ui.platformConfiguration') }}</CardTitle>
        <CardDescription>{{ $t('ui.platformNameDescriptionAndLogo') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-6">
        <section class="flex flex-col gap-4" aria-labelledby="platform-runtime-information">
          <div>
            <h2 id="platform-runtime-information" class="font-semibold">{{ $t('ui.platformInformation') }}</h2>
            <p class="mt-1 text-xs text-muted-foreground">{{ $t('ui.backendVersionsAndProjectContributors') }}</p>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-sm text-muted-foreground">{{ $t('ui.version') }}</span>
            <Badge variant="secondary" class="font-mono">{{ information?.version ?? '-' }}</Badge>
          </div>
          <div class="flex flex-col gap-2">
            <span class="text-sm text-muted-foreground">{{ $t('ui.contributor') }}</span>
            <div v-if="information?.contributors?.length" class="flex flex-wrap gap-2">
              <Avatar v-for="contributor in information.contributors" :key="contributor.id" class="size-8">
                <AvatarImage v-if="contributor.avatarUrl" :src="contributor.avatarUrl" :alt="contributor.id ?? ''" />
                <AvatarFallback>{{ contributor.id?.slice(0, 2) ?? '?' }}</AvatarFallback>
              </Avatar>
            </div>
            <span v-else class="text-sm text-muted-foreground">-</span>
          </div>
        </section>

        <Separator />

        <section aria-labelledby="platform-identity-configuration">
          <h2 id="platform-identity-configuration" class="sr-only">{{ $t('ui.platformConfiguration') }}</h2>
          <UiForm @submit.prevent="save">
            <FieldGroup>
              <Field>
                <FieldLabel for="platform-name">{{ $t('ui.platformName') }}</FieldLabel>
                <Input id="platform-name" v-model="name" required maxlength="100" />
              </Field>
              <Field>
                <FieldLabel for="platform-description">{{ $t('ui.platformDescription') }}</FieldLabel>
                <Textarea id="platform-description" v-model="description" rows="3" maxlength="500" />
              </Field>
              <Field>
                <FieldLabel>{{ $t('ui.logo') }}</FieldLabel>
                <div class="flex items-center gap-4">
                  <img
                    v-if="logoSrc"
                    :src="logoSrc"
                    :alt="$t('ui.platformLogo')"
                    class="size-16 rounded-md border object-contain"
                  >
                  <span v-else class="text-sm text-muted-foreground">{{ $t('ui.logoNotSetYet') }}</span>
                  <FileInput :ref="setLogoInputRef"  accept="image/*" class="hidden" @change="uploadLogo" />
                  <Button type="button" variant="outline" :disabled="logoUploading" @click="logoInput?.click()">
                    <Spinner v-if="logoUploading" data-icon="inline-start" />
                    <Upload v-else data-icon="inline-start" /> {{ $t('ui.uploadLogo') }} </Button>
                </div>
              </Field>
              <Field orientation="horizontal">
                <Button type="submit" :disabled="saving || !name.trim()">
                  <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('ui.saveConfiguration') }} </Button>
              </Field>
            </FieldGroup>
          </UiForm>
        </section>
      </CardContent>
    </Card>
  </div>
</template>
