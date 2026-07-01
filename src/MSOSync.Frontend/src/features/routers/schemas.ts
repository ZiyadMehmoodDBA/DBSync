import { z } from 'zod';
import type { RouterDto } from '../../shared/types';

export const ROUTER_TYPES = ['default'] as const;
export type RouterType = typeof ROUTER_TYPES[number];

const routerBase = z.object({
  routerId: z.string().trim().min(1, 'Router ID is required'),
  sourceNodeGroup: z.string().trim().min(1, 'Source group is required'),
  targetNodeGroup: z.string().trim().min(1, 'Target group is required'),
  routerType: z.enum(ROUTER_TYPES),
});

const sourceNotTarget = (x: { sourceNodeGroup: string; targetNodeGroup: string }) =>
  x.sourceNodeGroup !== x.targetNodeGroup;
const sourceNotTargetOpts = {
  message: 'Source and target groups must differ.',
  path: ['targetNodeGroup'] as PropertyKey[],
};

export const createRouterSchema = routerBase.refine(sourceNotTarget, sourceNotTargetOpts);
export type CreateRouterForm = z.infer<typeof createRouterSchema>;

export const updateRouterSchema = routerBase.omit({ routerId: true }).refine(sourceNotTarget, sourceNotTargetOpts);
export type UpdateRouterForm = z.infer<typeof updateRouterSchema>;

export function getDefaultValues(
  initialValues?: RouterDto,
  mode?: 'create' | 'edit',
): CreateRouterForm | UpdateRouterForm {
  if (mode === 'edit' && initialValues) {
    return {
      // Map response fields (sourceGroupId) to request fields (sourceNodeGroup)
      sourceNodeGroup: initialValues.sourceGroupId,
      targetNodeGroup: initialValues.targetGroupId,
      routerType: 'default',
    };
  }
  return {
    routerId: '',
    sourceNodeGroup: '',
    targetNodeGroup: '',
    routerType: 'default',
  };
}
