import { useTemplateVersions } from '../../features/configuration/hooks';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from './select';

interface Props {
  templateId: string;
  value: number | null;
  onChange: (v: number) => void;
}

export function TemplateVersionSelect({ templateId, value, onChange }: Props) {
  const { data: versions, isLoading } = useTemplateVersions(templateId);
  const published = versions?.filter((v) => !v.isDraft) ?? [];

  return (
    <Select
      value={value?.toString() ?? ''}
      onValueChange={(v) => onChange(parseInt(v, 10))}
      disabled={isLoading || !templateId}
    >
      <SelectTrigger>
        <SelectValue placeholder="Select version..." />
      </SelectTrigger>
      <SelectContent>
        {published.map((v) => (
          <SelectItem key={v.versionNumber} value={v.versionNumber.toString()}>
            v{v.versionNumber}
            {v.publishedAt ? ` — ${new Date(v.publishedAt).toLocaleDateString()}` : ''}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
