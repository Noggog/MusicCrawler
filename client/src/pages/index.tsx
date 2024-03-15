import Image from "next/image";
import { useQuery } from "@apollo/client";
import { GET_RECOMMENDATIONS } from "../graphql/queries";
// import client from '@apollo/client';
import { useEffect, useState } from "react";
import { RecommendationResponse } from "../graphql/dto";

export default function Home() {
  const [recommendationResponse, setRecommendationResponse] = useState<
    RecommendationResponse | undefined
  >(undefined);

  const { data, loading, error } = useQuery<RecommendationResponse>(
    GET_RECOMMENDATIONS,
    {
      onCompleted: (res) => {
        console.log("response:", JSON.stringify(res));
        setRecommendationResponse(data);
      },
      onError: (error) => {
        console.error("GraphQL error:", error);
      },
    }
  );

  useEffect(() => {
    console.log("aaa");
    console.log(`data:${JSON.stringify(data)}`);
  }, [recommendationResponse]);

  return (
    <div className="flex min-h-screen flex-col items-center p-24 bg-gray-600">
      <div className="w-8/12">
        {recommendationResponse?.recommendations?.map((it) => {
          return (
            <div className="flex justify-between my-5 bg-gradient-to-br from-gray-700 to-gray-900 rounded py-8 border-gray-800 border-2 shadow-xl">
              <div className="flex flex-col pl-10">
                <h2 className="text-white">Recommended artist: {it.key.artistName}</h2>
                <h2 className="text-white">
                  Source artists:{" "}
                  {it.sourceArtists
                    .map((x) => {
                      return x.artistName;
                    })
                    .join(", ")}
                </h2>
                </div>
                <div className="flex flex-row pr-10">
                  <button className="mx-3 px-5 bg-green-500 rounded">Accept</button>
                  <button className="mx-3 px-5 bg-red-400 rounded">Decline</button>
                </div>
              </div>
          );
        })}
      </div>
    </div>
  );
}
