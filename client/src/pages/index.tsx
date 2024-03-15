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
            <div className="flex flex-col">
              <div className="flex flex-col my-5 bg-gradient-to-br from-gray-500 to-gray-800 rounded py-8">
                <h2>Recommended artist: {it.key.artistName}</h2>
                <h2>
                  Source artists:{" "}
                  {it.sourceArtists
                    .map((x) => {
                      return x.artistName;
                    })
                    .join(", ")}
                </h2>
                <div className="flex">
                  <button>button1</button>
                  <button>button2</button>
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
