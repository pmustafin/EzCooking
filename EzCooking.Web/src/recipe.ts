import type { Document, Recipe } from './types'

export const documentOf = (recipe: Recipe): Document | null => recipe.document ? JSON.parse(recipe.document) as Document : null
