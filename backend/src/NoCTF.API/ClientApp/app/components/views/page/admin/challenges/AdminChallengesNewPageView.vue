<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminChallengesNewPageViewState } from '~/features/routes/admin/challenges/useAdminChallengesNewPage'

const viewProps = defineProps<{ state: AdminChallengesNewPageViewState }>()
const { canOrganize, title, mode, visibility, direction, description, definitionJson, saveErrors, pending, titleInvalid, directionInvalid, changeMode, submit, DefinitionEditor, onBlurDirection } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-8">
    <div>
      <h1 class="text-2xl font-semibold">{{ $t('ui.createANewQuestionTemplate') }}</h1>
      <p class="text-sm text-muted-foreground">{{ $t('ui.createAGlobalQuestionBankTemplateThatCanBeInstantiated') }}</p>
    </div>

    <Alert v-if="!canOrganize" variant="destructive">
      <AlertDescription>{{ $t('ui.organizerOrAdministratorRightsAreRequiredToCreateQuestionTemplates') }}</AlertDescription>
    </Alert>

    <Card v-else>
      <CardContent class="pt-6">
        <form novalidate @submit.prevent="submit">
          <Alert v-if="saveErrors.length" variant="destructive" class="mb-6">
            <AlertTitle>{{ $t('ui.unableToSaveTheChallengeTemplate') }}</AlertTitle>
            <AlertDescription class="flex flex-col gap-2">
              <span>{{ $t('ui.correctTheFollowingIssuesAndTryAgain') }}</span>
              <ul class="list-disc pl-5">
                <li v-for="message in saveErrors" :key="message">{{ message }}</li>
              </ul>
            </AlertDescription>
          </Alert>
          <Tabs default-value="basic">
            <TabsList>
              <TabsTrigger value="basic">{{ $t('ui.basicInformation') }}</TabsTrigger>
              <TabsTrigger value="definition">{{ $t('ui.questionDefinition') }}</TabsTrigger>
            </TabsList>
            <TabsContent value="basic">
              <FieldGroup>
                <Field :data-invalid="titleInvalid || undefined">
                  <FieldLabel for="title">{{ $t('ui.title') }}</FieldLabel>
                  <Input
                    id="title"
                    v-model="title"
                    required
                    maxlength="160"
                    :aria-invalid="titleInvalid || undefined"
                    :placeholder="$t('ui.forExampleGettingStartedWithWebSqlInjection')"
                  />
                </Field>
                <div class="grid gap-4 sm:grid-cols-2">
                  <Field>
                    <FieldLabel for="mode">{{ $t('ui.gameMode') }}</FieldLabel>
                    <Select :model-value="mode" @update:model-value="changeMode">
                      <SelectTrigger id="mode" class="w-full">
                        <SelectValue :placeholder="$t('ui.selectMode')" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectGroup>
                          <SelectItem value="Ctf">{{ $t('ui.ctf') }}</SelectItem>
                          <SelectItem value="Awd">{{ $t('ui.awd') }}</SelectItem>
                          <SelectItem value="Awdp">{{ $t('ui.awdp') }}</SelectItem>
                          <SelectItem value="Koh">{{ $t('ui.koh') }}</SelectItem>
                        </SelectGroup>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel for="visibility">{{ $t('ui.visibility') }}</FieldLabel>
                    <Select v-model="visibility">
                      <SelectTrigger id="visibility" class="w-full">
                        <SelectValue :placeholder="$t('ui.selectVisibility')" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectGroup>
                          <SelectItem value="Private">{{ $t('ui.privateOnlyAvailableToThePersonInChargeAdministrator') }}</SelectItem>
                          <SelectItem value="Shared">{{ $t('ui.shareAvailableToAllOrganizers') }}</SelectItem>
                        </SelectGroup>
                      </SelectContent>
                    </Select>
                  </Field>
                </div>
                <Field :data-invalid="directionInvalid || undefined">
                  <FieldLabel for="direction">{{ $t('ui.category') }}</FieldLabel>
                  <Input
                    id="direction"
                    v-model="direction"
                    @blur="onBlurDirection"
                    required
                    maxlength="96"
                    :aria-invalid="directionInvalid || undefined"
                    :placeholder="$t('ui.forExampleWebPwnMisc')"
                  />
                </Field>
                <Field>
                  <FieldLabel for="description">{{ $t('ui.question') }}</FieldLabel>
                  <Textarea id="description" v-model="description" rows="6" :placeholder="$t('ui.titleDescriptionSupportsMarkdown')" />
                </Field>
                <Field orientation="horizontal">
                  <Button type="submit" :disabled="pending">
                    <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('ui.createTemplate') }} </Button>
                  <Button type="button" variant="outline" as-child>
                    <NuxtLink to="/admin/challenges">{{ $t('ui.cancel') }}</NuxtLink>
                  </Button>
                </Field>
              </FieldGroup>
            </TabsContent>
            <TabsContent value="definition">
              <FieldGroup>
                <Alert>
                  <AlertDescription>{{ $t('ui.optionalYouCanConfigureItLaterAfterCreation') }}</AlertDescription>
                </Alert>
                <component :is="DefinitionEditor" v-model="definitionJson" :mode="mode" />
              </FieldGroup>
            </TabsContent>
          </Tabs>
        </form>
      </CardContent>
    </Card>
  </div>
</template>
