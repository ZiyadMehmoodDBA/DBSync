import { useState, useEffect } from 'react';
import { toast } from 'sonner';
import { useNodeManagement } from '../../NodeManagementProvider';
import { Step1NodeType }    from '../steps/Step1NodeType';
import { Step2Credentials } from '../steps/Step2Credentials';
import { Step3Network }     from '../steps/Step3Network';
import { Step4Review }      from '../steps/Step4Review';
import { Step5Complete }    from '../steps/Step5Complete';
import {
  loadWizardDraft,
  saveWizardDraft,
  clearWizardDraft,
} from '../../types/provision';
import type { ProvisionWizardDraft } from '../../types/provision';
import { useProvision } from '../../hooks/useProvision';

const EMPTY_DRAFT: ProvisionWizardDraft = { step: 1 };

const STEPS = ['Node Type', 'Credentials', 'Network', 'Review', 'Complete'];

function StepIndicator({ current }: { current: number }) {
  return (
    <div className="flex items-center gap-2 mb-6 flex-wrap">
      {STEPS.map((label, i) => {
        const n      = i + 1;
        const active = n === current;
        const done   = n < current;
        return (
          <div key={n} className="flex items-center gap-1">
            <div
              className={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-medium ${
                done
                  ? 'bg-green-600 text-white'
                  : active
                  ? 'bg-blue-600 text-white'
                  : 'bg-neutral-200 text-neutral-500 dark:bg-neutral-700 dark:text-neutral-400'
              }`}
            >
              {done ? '✓' : n}
            </div>
            <span
              className={`text-xs hidden sm:block ${
                active ? 'font-medium' : 'text-neutral-400'
              }`}
            >
              {label}
            </span>
            {i < STEPS.length - 1 && (
              <div className="w-4 h-px bg-neutral-200 dark:bg-neutral-700 mx-1" />
            )}
          </div>
        );
      })}
    </div>
  );
}

export function ProvisionWizard() {
  const { setWizardDraft } = useNodeManagement();
  const provision = useProvision();

  const [step, setStep]             = useState(1);
  const [draft, setDraft]           = useState<ProvisionWizardDraft>(EMPTY_DRAFT);
  const [provisionResult, setResult] =
    useState<{ nodeId: string; token: string } | null>(null);
  const [draftOffered, setDraftOffered] = useState(false);

  useEffect(() => {
    if (draftOffered) return;
    setDraftOffered(true);
    const saved = loadWizardDraft();
    if (saved) {
      toast('Resume previous draft?', {
        action: {
          label: 'Resume',
          onClick: () => {
            setDraft(saved);
            setStep(saved.step ?? 1);
          },
        },
        cancel: {
          label: 'Start fresh',
          onClick: () => clearWizardDraft(),
        },
        duration: 10_000,
      });
    }
  }, [draftOffered]);

  function patch(partial: Partial<ProvisionWizardDraft>) {
    setDraft(prev => {
      const next = { ...prev, ...partial };
      setWizardDraft(next);
      saveWizardDraft(next);
      return next;
    });
  }

  function advance() {
    const next = step + 1;
    patch({ step: next });
    setStep(next);
  }

  function goBack() {
    const prev = step - 1;
    patch({ step: prev });
    setStep(prev);
  }

  function cancel() {
    clearWizardDraft();
    setDraft(EMPTY_DRAFT);
    setWizardDraft(null);
    setStep(1);
    setResult(null);
  }

  async function handleSubmit() {
    try {
      const result = await provision.mutateAsync({
        nodeName:    draft.nodeName!,
        externalId:  draft.externalId!,
        nodeType:    draft.nodeType!,
        dbServer:    draft.dbServer!,
        dbName:      draft.dbName!,
        groupId:     draft.groupId,
        description: draft.description,
      });
      setResult(result);
      clearWizardDraft();
      setWizardDraft(null);
      setStep(5);
    } catch {
      toast.error('Provisioning failed. Please try again.');
    }
  }

  function restart() {
    setDraft(EMPTY_DRAFT);
    setResult(null);
    setStep(1);
  }

  return (
    <div className="max-w-2xl mx-auto py-8 px-4">
      <div className="flex justify-between items-start mb-4">
        <h1 className="text-lg font-semibold">Provision New Node</h1>
        {step < 5 && (
          <button
            onClick={cancel}
            className="text-xs text-neutral-400 hover:text-neutral-600"
          >
            Cancel
          </button>
        )}
      </div>
      <StepIndicator current={step} />
      {step === 1 && (
        <Step1NodeType draft={draft} onChange={patch} onNext={advance} />
      )}
      {step === 2 && (
        <Step2Credentials draft={draft} onChange={patch} onNext={advance} onBack={goBack} />
      )}
      {step === 3 && (
        <Step3Network draft={draft} onChange={patch} onNext={advance} onBack={goBack} />
      )}
      {step === 4 && (
        <Step4Review
          draft={draft}
          onSubmit={handleSubmit}
          onBack={goBack}
          isLoading={provision.isPending}
        />
      )}
      {step === 5 && (
        <Step5Complete
          nodeId={provisionResult?.nodeId ?? ''}
          token={provisionResult?.token ?? null}
          onRestart={restart}
        />
      )}
    </div>
  );
}
