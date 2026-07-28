import { useState, useEffect } from 'react';
import { ShieldCheck, ExternalLink } from 'lucide-react';
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from '../../components/ui/sheet';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../../components/ui/select';
import { Button } from '../../components/ui/button';
import { Badge } from '../../components/ui/badge';
import { Separator } from '../../components/ui/separator';
import { MarketplaceStarRating } from './MarketplaceStarRating';
import { useMarketplacePlugin } from '../../shared/hooks/useMarketplace';

interface MarketplacePluginDrawerProps {
  pluginId:     string | null;   // null = closed
  onClose:      () => void;
  isInstalled:  boolean;
  onInstall:    (id: string, version: string, name: string) => void;
  isInstalling: boolean;
}

export function MarketplacePluginDrawer({
  pluginId,
  onClose,
  isInstalled,
  onInstall,
  isInstalling,
}: MarketplacePluginDrawerProps) {
  const { data: detail, isLoading } = useMarketplacePlugin(pluginId);
  const [selectedVersion, setSelectedVersion] = useState<string>('');

  // Reset selected version when a new plugin is opened
  useEffect(() => {
    if (detail) {
      setSelectedVersion(detail.latestVersion);
    }
  }, [detail?.id, detail?.latestVersion]);

  const nonDeprecatedVersions = detail?.versions.filter(v => !v.deprecated) ?? [];
  const deprecatedVersions    = detail?.versions.filter(v => v.deprecated)  ?? [];

  const selectedVersionDetail = detail?.versions.find(v => v.version === selectedVersion);

  return (
    <Sheet open={pluginId !== null} onOpenChange={(open) => { if (!open) onClose(); }}>
      <SheetContent side="right" className="w-[480px] sm:w-[540px] overflow-y-auto">
        {isLoading && (
          <div className="flex items-center justify-center h-32 text-sm text-neutral-500">
            Loading…
          </div>
        )}

        {detail && (
          <>
            <SheetHeader className="pb-4">
              <div className="flex items-start gap-3">
                <div>
                  <SheetTitle className="flex items-center gap-2 text-left">
                    {detail.name}
                    {detail.verified && (
                      <ShieldCheck className="h-4 w-4 text-blue-500 shrink-0" aria-label="Verified publisher" />
                    )}
                  </SheetTitle>
                  <p className="text-sm text-neutral-500 mt-0.5">{detail.author}</p>
                </div>
                <Badge variant="secondary" className="ml-auto shrink-0">
                  {detail.category}
                </Badge>
              </div>

              <div className="flex items-center gap-4 pt-2">
                <MarketplaceStarRating rating={detail.rating} ratingCount={detail.ratingCount} />
                <span className="text-xs text-neutral-500">
                  {new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 }).format(detail.downloadCount)} downloads
                </span>
              </div>
            </SheetHeader>

            <Separator />

            <div className="space-y-4 py-4">
              {/* Description */}
              <div>
                <h3 className="text-sm font-medium mb-2">Description</h3>
                <p className="text-sm text-neutral-600 dark:text-neutral-400 whitespace-pre-wrap">
                  {detail.description}
                </p>
              </div>

              {/* Tags */}
              {detail.tags.length > 0 && (
                <div className="flex flex-wrap gap-1.5">
                  {detail.tags.map(tag => (
                    <span
                      key={tag}
                      className="rounded-full px-2 py-0.5 text-xs bg-neutral-100 dark:bg-neutral-800 text-neutral-600 dark:text-neutral-300"
                    >
                      {tag}
                    </span>
                  ))}
                </div>
              )}

              {/* Links */}
              {(detail.projectUrl || detail.licenseId) && (
                <div className="flex items-center gap-4 text-xs">
                  {detail.projectUrl && (
                    <a
                      href={detail.projectUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="flex items-center gap-1 text-blue-600 hover:underline dark:text-blue-400"
                    >
                      <ExternalLink className="h-3 w-3" />
                      Project page
                    </a>
                  )}
                  {detail.licenseId && (
                    <span className="text-neutral-500">License: {detail.licenseId}</span>
                  )}
                </div>
              )}

              <Separator />

              {/* Version selector + install */}
              <div>
                <h3 className="text-sm font-medium mb-3">Install version</h3>
                <div className="flex items-center gap-3">
                  <Select
                    value={selectedVersion}
                    onValueChange={setSelectedVersion}
                  >
                    <SelectTrigger className="flex-1">
                      <SelectValue placeholder="Select version" />
                    </SelectTrigger>
                    <SelectContent>
                      {nonDeprecatedVersions.map(v => (
                        <SelectItem key={v.version} value={v.version}>
                          v{v.version}
                        </SelectItem>
                      ))}
                      {deprecatedVersions.map(v => (
                        <SelectItem
                          key={v.version}
                          value={v.version}
                          className="text-neutral-400"
                        >
                          v{v.version} (deprecated)
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>

                  {isInstalled ? (
                    <Badge variant="secondary" className="bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400 whitespace-nowrap">
                      Installed
                    </Badge>
                  ) : (
                    <Button
                      onClick={() => onInstall(detail.id, selectedVersion, detail.name)}
                      disabled={isInstalling || !selectedVersion}
                      aria-label={`Install ${detail.name}`}
                    >
                      {isInstalling ? 'Installing…' : 'Install'}
                    </Button>
                  )}
                </div>
              </div>

              {/* Release notes */}
              {selectedVersionDetail?.releaseNotes && (
                <div>
                  <h3 className="text-sm font-medium mb-2">
                    Release notes — v{selectedVersionDetail.version}
                  </h3>
                  <pre className="whitespace-pre-wrap text-xs font-mono bg-neutral-50 dark:bg-neutral-800/50 rounded-md p-3 text-neutral-700 dark:text-neutral-300">
                    {selectedVersionDetail.releaseNotes}
                  </pre>
                </div>
              )}
            </div>
          </>
        )}
      </SheetContent>
    </Sheet>
  );
}
