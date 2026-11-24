import { redirect } from "next/navigation";

/** /admin has no page of its own: the pending agencies are the admin's to-do list. */
export default function AdminHome() {
  redirect("/admin/agencies");
}
