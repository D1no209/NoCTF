<script setup lang="ts">
const props = withDefaults(defineProps<{
  title: string
  /** 折叠状态下展示的副标题提示。 */
  hint?: string
  /** false 时固定展开,不渲染折叠开关。 */
  collapsible?: boolean
  /** 初始是否展开;编辑存量内容时可由父组件按数据非空传入 true。 */
  defaultOpen?: boolean
}>(), {
  hint: undefined,
  collapsible: true,
  defaultOpen: false,
})

const openValue = ref(props.defaultOpen ? 'content' : '')
</script>

<template>
  <section v-if="!collapsible" class="rounded-md border">
    <header class="px-4 pt-3">
      <h3 class="text-sm font-medium">{{ title }}</h3>
      <p v-if="hint" class="text-muted-foreground mt-0.5 text-xs">{{ hint }}</p>
    </header>
    <div class="px-4 pb-4 pt-3">
      <FieldGroup>
        <slot />
      </FieldGroup>
    </div>
  </section>
  <Accordion v-else v-model="openValue" type="single" collapsible class="rounded-md border">
    <AccordionItem value="content" class="border-b-0">
      <AccordionTrigger class="px-4 py-3 hover:no-underline">
        <span class="flex flex-col gap-0.5">
          <span>{{ title }}</span>
          <span v-if="hint" class="text-muted-foreground text-xs font-normal">{{ hint }}</span>
        </span>
      </AccordionTrigger>
      <AccordionContent class="px-4 pb-4">
        <FieldGroup>
          <slot />
        </FieldGroup>
      </AccordionContent>
    </AccordionItem>
  </Accordion>
</template>
