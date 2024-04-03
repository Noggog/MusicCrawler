import Image from "next/image";
import { useLazyQuery, useQuery } from "@apollo/client";
import { GET_RECOMMENDATIONS } from "../graphql/queries";
import { ACCUMULATE_RECOMMENDATIONS } from "../graphql/queries";
// import client from '@apollo/client';
import { useEffect, useState } from "react";
import { RecommendationResponse } from "../graphql/dto";

// function useAccumulateRecommendations() {
//   const { data, error } = useQuery(ACCUMULATE_RECOMMENDATIONS, {
//     onCompleted: (res) => {
//       console.log("response:", JSON.stringify(res));
//     },
//     onError: (error) => {
//       console.error("GraphQL error:", error);
//     },
//   });

//   return { data, error };
// }

export default function Home() {
  const [recommendationResponse, setRecommendationResponse] = useState<
    RecommendationResponse | undefined
  >(undefined);

  // const accumulateRecommendations = async () => {
  //   const { data, error } = useQuery(ACCUMULATE_RECOMMENDATIONS, {
  //     onCompleted: (res) => {
  //       console.log("response:", JSON.stringify(res));
  //     },
  //     onError: (error) => {
  //       console.error("GraphQL error:", error);
  //     },
  //   });

  //   return { data, error };
  // };

  // const handleClick = async () => {
  //   console.log(`handleClick`);
  //   await useQuery<Boolean>(ACCUMULATE_RECOMMENDATIONS, {
  //     onCompleted: (res) => {
  //       console.log("response:", JSON.stringify(res));
  //     },
  //     onError: (error) => {
  //       console.error("GraphQL error:", error);
  //     },
  //   });
  // };
  
  // const handleClick = async () => {
  //   try {
  //     const response = await fetch(ACCUMULATE_RECOMMENDATIONS);
  //     const data = await response.json();
  //     console.log("response:", JSON.stringify(data));
  //   } catch (error) {
  //     console.error("GraphQL error:", error);
  //   }
  // };

  const [
    accumulateRecommendations
  ] = useLazyQuery(ACCUMULATE_RECOMMENDATIONS);

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
    console.log(`data:${JSON.stringify(data)}`);
    console.log(`count:${data?.recommendations?.length}`);
  }, [recommendationResponse]);

  // useEffect(() => {
  //   console.log(`data2:${JSON.stringify(data2)}`);
  // }, [data2]);

  return (
    <main className="flex min-h-screen flex-col items-center pt-24 bg-gray-700">
      <div>
        <h1 className="text-white font-semibold text-[40px] mb-10">
          Recommended Artists
        </h1>
      </div>
      <div className="flex items-center justify-center flex-col md:flex-row sm:pr-10 mt-5 sm:mt-0">
        <button
          className="w-22 mx-3 my-4 px-5 py-2 bg-green-500 rounded"
          // onClick={() => {
          //   console.log("Accumulate Recommendations");
          //   handleClick();
          // }}
          onClick={() => {
            console.log("about to accumulateRecommendations..")
            accumulateRecommendations()
            console.log("done with accumulateRecommendations.")
          }}
        >
          Accumulate Recommendations
        </button>
      </div>
      <div className="flex flex-col w-[80%] items-center">
        {recommendationResponse?.recommendations?.map((it) => {
          return (
            <div className="flex flex-col sm:flex-row max-w-[800px] w-[100%] justify-between my-5 bg-gradient-to-br from-gray-700 to-gray-900 rounded py-8 border-gray-800 border-2 shadow-xl">
              <div className="flex flex-col justify-around pl-5 sm:pl-10 w-400px">
                <h2 className="text-white">
                  Recommended artist: {it.key.artistName}
                </h2>
                <h2 className="text-white">
                  Source artists:{" "}
                  {it.sourceArtists
                    .map((x) => {
                      return x.artistName;
                    })
                    .join(", ")}
                </h2>
              </div>
              <div className="flex items-center justify-center flex-col md:flex-row sm:pr-10 mt-5 sm:mt-0">
                <button
                  className="w-22 mx-3 my-4 px-5 py-2 bg-green-500 rounded"
                  onClick={() => console.log("Accepted")}
                >
                  Accept
                </button>
                <button
                  className="w-22 mx-3 px-5 py-2 bg-red-400 rounded"
                  onClick={() => console.log("Ooof declined")}
                >
                  Decline
                </button>
              </div>
            </div>
          );
        })}
      </div>
    </main>
  );
}
