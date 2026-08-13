import { parseApiError } from '../utils/api-error'
import { translate } from '../utils/i18n'

export function challengeTemplateWriteErrorMessage(error: unknown): string {
  const parsed = parseApiError(error)
  const invalidDefinitionField = Object.keys(parsed.fieldErrors ?? {})
    .some(field => field.toLowerCase() === 'definitionjson')

  if (invalidDefinitionField || /definition|schemaVersion/i.test(parsed.message))
    return translate('题目定义格式无效,请检查题目定义配置')

  return parsed.message
}
