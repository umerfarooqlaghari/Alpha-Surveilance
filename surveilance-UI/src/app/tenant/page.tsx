import { redirect } from 'next/navigation';

export default function TenantRootRedirect() {
    redirect('/tenant/analytics');
}
