import {
  Baby, BadgePercent, BookOpen, Briefcase, Bus, Car, CircleHelp, CirclePlus, Clapperboard, Coffee, Droplets, Dumbbell,
  Flame, Fuel, Gamepad2, Gift, GraduationCap, HeartPulse, House, Landmark, Music, PawPrint, PiggyBank, Pill, Plane,
  Receipt, Repeat, RotateCcw, Shirt, ShoppingBag, ShoppingBasket, Smartphone, Sparkles, Store, Tag, Train, Utensils,
  Wallet, Wifi, Wrench, Zap, type LucideIcon,
} from 'lucide-react';

/** Icons a category can use. Keys are stored in the DB (kebab-case Lucide names). */
export const CATEGORY_ICONS: Record<string, LucideIcon> = {
  'shopping-basket': ShoppingBasket,
  utensils: Utensils,
  coffee: Coffee,
  car: Car,
  fuel: Fuel,
  bus: Bus,
  train: Train,
  plane: Plane,
  'shopping-bag': ShoppingBag,
  shirt: Shirt,
  store: Store,
  receipt: Receipt,
  zap: Zap,
  droplets: Droplets,
  flame: Flame,
  wifi: Wifi,
  smartphone: Smartphone,
  home: House,
  wrench: Wrench,
  'heart-pulse': HeartPulse,
  pill: Pill,
  dumbbell: Dumbbell,
  clapperboard: Clapperboard,
  music: Music,
  'gamepad-2': Gamepad2,
  repeat: Repeat,
  'graduation-cap': GraduationCap,
  'book-open': BookOpen,
  sparkles: Sparkles,
  baby: Baby,
  'paw-print': PawPrint,
  gift: Gift,
  'badge-percent': BadgePercent,
  landmark: Landmark,
  briefcase: Briefcase,
  'rotate-ccw': RotateCcw,
  'circle-plus': CirclePlus,
  'piggy-bank': PiggyBank,
  wallet: Wallet,
  tag: Tag,
};

export function CategoryIcon({ name, className }: { name: string | null | undefined; className?: string }) {
  const Icon = (name && CATEGORY_ICONS[name]) || (name ? Tag : CircleHelp);
  return <Icon className={className} aria-hidden strokeWidth={1.75} />;
}
