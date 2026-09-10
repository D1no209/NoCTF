export function terminalEnterAction(event: Pick<KeyboardEvent, 'key' | 'shiftKey' | 'isComposing' | 'repeat' | 'keyCode'>, state: { composing: boolean; multiple: boolean; blocked: boolean; value: string }) {
  if (event.key !== 'Enter' || event.isComposing || state.composing || event.keyCode === 229) return 'ignore'
  if (event.shiftKey && state.multiple && !state.blocked) return 'newline'
  return !state.blocked && !event.repeat && state.value.trim() ? 'submit' : 'prevent'
}
