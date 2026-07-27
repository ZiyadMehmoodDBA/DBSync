import { Star } from 'lucide-react';
import { cn } from '../../lib/utils';

interface MarketplaceStarRatingProps {
  rating:      number;   // 0.0–5.0
  ratingCount: number;
  showCount?:  boolean;  // default true
}

export function MarketplaceStarRating({
  rating,
  ratingCount,
  showCount = true,
}: MarketplaceStarRatingProps) {
  const fullStars = Math.floor(rating);
  const fractional = rating - fullStars;

  return (
    <span
      className="flex items-center gap-0.5"
      aria-label={`Rated ${rating} out of 5`}
    >
      {Array.from({ length: 5 }, (_, i) => {
        const isFull    = i < fullStars;
        const isPartial = i === fullStars && fractional > 0;
        return (
          <Star
            key={i}
            className={cn(
              'h-3 w-3',
              isFull
                ? 'fill-amber-400 text-amber-400'
                : isPartial
                  ? 'fill-amber-400 text-amber-400'
                  : 'fill-none text-neutral-300 dark:text-neutral-600',
            )}
            style={isPartial ? { opacity: 0.3 + fractional * 0.7 } : undefined}
          />
        );
      })}
      {showCount && (
        <span className="ml-1 text-xs text-neutral-500 dark:text-neutral-400">
          ({ratingCount})
        </span>
      )}
    </span>
  );
}
