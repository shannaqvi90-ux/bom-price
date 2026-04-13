import { type HTMLAttributes } from "react";
import { cn } from "@/lib/cn";

export function Card({
  className,
  ...props
}: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        "rounded-lg border border-border bg-card text-card-foreground shadow-sm",
        className,
      )}
      {...props}
    />
  );
}

export function CardHeader(props: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("p-6 pb-2", props.className)} {...props} />;
}

export function CardContent(props: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("p-6 pt-2", props.className)} {...props} />;
}

export function CardTitle(props: HTMLAttributes<HTMLHeadingElement>) {
  return (
    <h2
      className={cn("text-xl font-semibold tracking-tight", props.className)}
      {...props}
    />
  );
}
