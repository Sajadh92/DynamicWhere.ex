"use client";

import { DocSearch } from "@docsearch/react";
import "@docsearch/css";

const APP_ID = process.env.NEXT_PUBLIC_DOCSEARCH_APP_ID || "REPLACE_APP_ID";
const API_KEY = process.env.NEXT_PUBLIC_DOCSEARCH_API_KEY || "REPLACE_SEARCH_API_KEY";
const INDEX_NAME = process.env.NEXT_PUBLIC_DOCSEARCH_INDEX_NAME || "dynamicwhere";

export default function Search() {
  return (
    <DocSearch
      appId={APP_ID}
      apiKey={API_KEY}
      indexName={INDEX_NAME}
      placeholder="Search docs…"
    />
  );
}
