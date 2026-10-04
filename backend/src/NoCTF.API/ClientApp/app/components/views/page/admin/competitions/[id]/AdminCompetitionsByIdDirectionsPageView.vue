<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdDirectionsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdDirectionsPage'
const viewProps = defineProps<{ state: AdminCompetitionsByIdDirectionsPageViewState }>()
const { Plus, Trash2, RefreshCw, items, loading, saving, error, canWrite, canSave, load, add, remove, save, canRemove, isLucideIconName } = toRefs(viewProps.state)
</script>
<template>
  <div class="flex flex-col gap-4">
    <header class="flex items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold">{{ $t('directionSettings.title') }}</h1>
      <Button variant="ghost" size="icon" :disabled="loading || saving" :aria-label="$t('common.label.refresh')" @click="load"><RefreshCw /></Button>
    </header>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <Card>
      <CardContent class="pt-6">
        <UiForm @submit.prevent="save">
          <FieldGroup>
            <Skeleton v-if="loading" class="h-24" />
            <div v-for="item in items" v-else :key="item.id" class="flex items-start gap-3">
              <LucideIcon :name="item.icon || 'flag'" class="mt-8 shrink-0 text-primary" />
              <div class="grid min-w-0 flex-1 gap-3 sm:grid-cols-2">
                <Field>
                  <FieldLabel :for="`direction-name-${item.id}`">{{ $t('directionSettings.name') }}</FieldLabel>
                  <Input :id="`direction-name-${item.id}`" v-model="item.name" maxlength="96" :readonly="!canWrite || saving" required />
                </Field>
                <Field :data-invalid="!isLucideIconName(item.icon || '')">
                  <FieldLabel :for="`direction-icon-${item.id}`">{{ $t('directionSettings.icon') }}</FieldLabel>
                  <div class="flex items-center gap-2"><span class="text-sm text-muted-foreground">{{ $t('directionSettings.prefix') }}</span><Input :id="`direction-icon-${item.id}`" v-model="item.icon" maxlength="80" :readonly="!canWrite || saving" :aria-invalid="!isLucideIconName(item.icon || '')" :placeholder="$t('directionSettings.iconPlaceholder')" required /></div>
                  <FieldError v-if="!isLucideIconName(item.icon || '')">{{ $t('directionSettings.invalidIcon') }}</FieldError>
                </Field>
              </div>
              <Hint :content="canRemove(item) ? $t('common.action.delete') : $t('directionSettings.inUse')"><Button v-if="canWrite" type="button" variant="ghost" size="icon" class="mt-6 shrink-0" :disabled="saving || !canRemove(item)" :aria-label="$t('common.action.delete')" @click="remove(item)"><Trash2 /></Button></Hint>
            </div>
            <FieldDescription>{{ $t('directionSettings.iconHelp') }}</FieldDescription>
            <div v-if="canWrite" class="flex flex-wrap justify-between gap-3">
              <Button type="button" variant="outline" :disabled="loading || saving || items.length >= 64" @click="add"><Plus data-icon="inline-start" />{{ $t('directionSettings.add') }}</Button>
              <Button type="submit" :disabled="!canSave"><Spinner v-if="saving" data-icon="inline-start" />{{ $t('administration.label.saveSettings') }}</Button>
            </div>
          </FieldGroup>
        </UiForm>
      </CardContent>
    </Card>
  </div>
</template>
